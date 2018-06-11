// <copyright file="Program.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using FubarDev.FtpServer;
using FubarDev.FtpServer.FileSystem.DotNet;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Mono.Options;

using NLog.Extensions.Logging;

namespace TestFtpServer
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var options = new TestFtpServerOptions();

            var optionSet = new CommandSet("ftpserver")
            {
                "usage: ftpserver [OPTIONS] <COMMAND> [COMMAND-OPTIONS]",
                { "h|?|help", "Show help", v => options.ShowHelp = v != null },
                "Server",
                { "a|address=", "Sets the IP address or host name", v => options.ServerAddress = v },
                { "p|port=", "Sets the listen port", v => options.Port = Convert.ToInt32(v) },
                "FTPS",
                { "c|certificate=", "Set the SSL certificate", v => options.ServerCertificateFile =v },
                { "P|password=", "Password for the SSL certificate", v => options.ServerCertificatePassword = v },
                { "i|implicit", "Use implicit FTPS", v => options.ImplicitFtps = XmlConvert.ToBoolean(v.ToLowerInvariant()) },
                "Backends",
                new Command("filesystem", "Use the System.IO file system access")
                {
                    Options = new OptionSet()
                    {
                        "usage: ftpserver filesystem [ROOT-DIRECTORY]",
                    },
                    Run = a => RunWithFileSystem(a.ToArray(), options),
                },
                new CommandSet("google-drive")
                {
                    new Command("user", "Use a users Google Drive as file system")
                    {
                        Options = new OptionSet()
                        {
                            "usage: ftpserver google-drive user <CLIENT-SECRETS-FILE> <USERNAME>",
                            { "r|refresh", "Refresh the access token", v => options.RefreshToken = v != null },
                        },
                        Run = a => RunWithGoogleDriveUser(a.ToArray(), options).Wait(),
                    },
                    new Command("service", "Use a users Google Drive with a service account")
                    {
                        Options = new OptionSet()
                        {
                            "usage: ftpserver google-drive service <SERVICE-CREDENTIAL-FILE>",
                        },
                        Run = a => RunWithGoogleDriveService(a.ToArray(), options),
                    },
                },
            };

            if (args.Length == 0)
            {
                return optionSet.Run(new[] { "filesystem" });
            }

            return optionSet.Run(args);
        }

        private static void RunWithFileSystem(string[] args, TestFtpServerOptions options)
        {
            options.Validate();
            var rootDir =
                args.Length != 0 ? args[0] : Path.Combine(Path.GetTempPath(), "TestFtpServer");
            var builder = CreateHostBuilder(options)
                .UseContentRoot(rootDir)
                .ConfigureServices(
                    services => services
                        .AddOptions<DotNetFileSystemOptions>()
                        .Configure<IHostingEnvironment>((opt, env) => opt.RootPath = env.ContentRootPath))
                .AddFtpServer(sb => Configure(sb).UseDotNetFileSystem());
            Run(builder);
        }

        private static async Task RunWithGoogleDriveUser(string[] args, TestFtpServerOptions options)
        {
            options.Validate();
            if (args.Length != 2)
            {
                throw new Exception("This command requires two arguments: <CLIENT-SECRETS-FILE> <USERNAME>");
            }

            var clientSecretsFile = args[0];
            var userName = args[1];

            UserCredential credential;
            using (var secretsSource = new FileStream(clientSecretsFile, FileMode.Open))
            {
                var secrets = GoogleClientSecrets.Load(secretsSource);
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    secrets.Secrets,
                    new[] { DriveService.Scope.DriveFile, DriveService.Scope.Drive },
                    userName,
                    CancellationToken.None);
                if (options.RefreshToken)
                {
                    await credential.RefreshTokenAsync(CancellationToken.None);
                }
            }

            var builder = CreateHostBuilder(options)
                .AddFtpServer(sb => Configure(sb).UseGoogleDrive(credential));
            Run(builder);
        }

        private static void RunWithGoogleDriveService(string[] args, TestFtpServerOptions options)
        {
            options.Validate();
            if (args.Length != 1)
            {
                throw new Exception("This command requires one argument: <SERVICE-CREDENTIAL-FILE>");
            }

            var serviceCredentialFile = args[0];
            var credential = GoogleCredential
                .FromFile(serviceCredentialFile)
                .CreateScoped(DriveService.Scope.Drive, DriveService.Scope.DriveFile);

            var builder = CreateHostBuilder(options)
                .AddFtpServer(sb => Configure(sb).UseGoogleDrive(credential));
            Run(builder);
        }

        private static void Run(IHostBuilder hostBuilder)
        {
            try
            {
                hostBuilder.RunConsoleAsync().Wait();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
        }

        private static IHostBuilder CreateHostBuilder(TestFtpServerOptions options)
        {
            NLog.LogManager.LoadConfiguration("NLog.config");

            return new HostBuilder()
                .ConfigureLogging(
                    lb => lb
                        .SetMinimumLevel(LogLevel.Trace)
                        .AddNLog(
                            new NLogProviderOptions
                            {
                                CaptureMessageTemplates = true,
                                CaptureMessageProperties = true
                            }))
                .ConfigureServices(services => Configure(services, options));
        }

        private static void Configure(IServiceCollection services, TestFtpServerOptions options)
        {
            services
                .AddOptions()
                .Configure<AuthTlsOptions>(
                    opt =>
                    {
                        if (options.ServerCertificateFile != null)
                        {
                            opt.ServerCertificate = new X509Certificate2(
                                options.ServerCertificateFile,
                                options.ServerCertificatePassword);
                        }
                    })
                .Configure<FtpConnectionOptions>(opt => opt.DefaultEncoding = Encoding.ASCII)
                .Configure<FtpServerOptions>(
                    opt =>
                    {
                        opt.ServerAddress = options.ServerAddress ?? "localhost";
                        opt.Port = options.GetPort();
                    });

            if (options.ImplicitFtps)
            {
                services.Decorate<FtpServer>(
                    (ftpServer, serviceProvider) =>
                    {
                        var authTlsOptions = serviceProvider.GetRequiredService<IOptions<AuthTlsOptions>>();

                        // Use an implicit SSL connection (without the AUTHTLS command)
                        ftpServer.ConfigureConnection += (s, e) =>
                        {
                            var sslStream = new SslStream(e.Connection.OriginalStream);
                            sslStream.AuthenticateAsServer(authTlsOptions.Value.ServerCertificate);
                            e.Connection.SocketStream = sslStream;
                        };

                        return ftpServer;
                    });
            }
        }

        private static IFtpServerBuilder Configure(IFtpServerBuilder builder)
        {
            return builder.EnableAnonymousAuthentication();
        }
    }
}
