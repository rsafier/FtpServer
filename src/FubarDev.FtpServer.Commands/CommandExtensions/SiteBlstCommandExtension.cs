// <copyright file="SiteBlstCommandExtension.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.BackgroundTransfer;

using JetBrains.Annotations;

using Microsoft.Extensions.Logging;

namespace FubarDev.FtpServer.CommandExtensions
{
    /// <summary>
    /// The implementation of the <code>SITE BLST</code> command.
    /// </summary>
    public class SiteBlstCommandExtension : FtpCommandHandlerExtension
    {
        [NotNull]
        private readonly IFtpServer _server;

        [CanBeNull]
        private readonly ILogger<SiteBlstCommandExtension> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SiteBlstCommandExtension"/> class.
        /// </summary>
        /// <param name="connection">The connection this instance is used for.</param>
        /// <param name="server">The FTP server.</param>
        /// <param name="logger">The logger.</param>
        public SiteBlstCommandExtension([NotNull] IFtpConnection connection, [NotNull] IFtpServer server, [CanBeNull] ILogger<SiteBlstCommandExtension> logger = null)
            : base(connection, "SITE", "BLST")
        {
            _server = server;
            _logger = logger;
        }

        /// <inheritdoc/>
        public override bool? IsLoginRequired { get; set; } = true;

        /// <inheritdoc/>
        public override async Task<FtpResponse> Process(FtpCommand command, CancellationToken cancellationToken)
        {
            var mode = (string.IsNullOrEmpty(command.Argument) ? "data" : command.Argument).ToLowerInvariant();

            switch (mode)
            {
                case "data":
                    return await SendBlstWithDataConnection(cancellationToken).ConfigureAwait(false);
                case "control":
                case "direct":
                    return await SendBlstDirectly(cancellationToken).ConfigureAwait(false);
            }

            return new FtpResponse(501, $"Mode {mode} not supported.");
        }

        private async Task<FtpResponse> SendBlstDirectly(CancellationToken cancellationToken)
        {
            var taskStates = _server.GetBackgroundTaskStates();
            if (taskStates.Count == 0)
            {
                return new FtpResponse(211, "No background tasks");
            }

            await Connection.WriteAsync("211-Active background tasks:", cancellationToken).ConfigureAwait(false);
            foreach (var line in GetLines(taskStates))
            {
                await Connection.WriteAsync($" {line}", cancellationToken).ConfigureAwait(false);
            }

            return new FtpResponse(211, "END");
        }

        private async Task<FtpResponse> SendBlstWithDataConnection(CancellationToken cancellationToken)
        {
            await Connection.WriteAsync(new FtpResponse(150, "Opening data connection."), cancellationToken).ConfigureAwait(false);

            return await Connection.SendResponseAsync(
                ExecuteSend,
                ex =>
                {
                    _logger?.LogError(ex, ex.Message);
                    return new FtpResponse(425, "Can't open data connection.");
                }).ConfigureAwait(false);
        }

        private async Task<FtpResponse> ExecuteSend(TcpClient responseSocket)
        {
            var encoding = Data.NlstEncoding ?? Connection.Encoding;
            var responseStream = responseSocket.GetStream();
            using (var stream = await Connection.CreateEncryptedStream(responseStream).ConfigureAwait(false))
            {
                using (var writer = new StreamWriter(stream, encoding, 4096, true)
                {
                    NewLine = "\r\n",
                })
                {
                    foreach (var line in GetLines(_server.GetBackgroundTaskStates()))
                    {
                        Connection.Log?.LogDebug(line);
                        await writer.WriteLineAsync(line).ConfigureAwait(false);
                    }
                }
            }

            return new FtpResponse(250, "Closing data connection.");
        }

        private IEnumerable<string> GetLines(IEnumerable<BackgroundTransferInfo> entries)
        {
            foreach (var entry in entries)
            {
                var line = new StringBuilder($"{entry.Status.ToString().PadRight(12)} {entry.FileName}");
                if (entry.Status == BackgroundTransferStatus.Transferring)
                {
                    line.Append($" ({entry.Transferred})");
                }

                yield return line.ToString();
            }
        }
    }
}
