// <copyright file="HostBuilderExtensions.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;

using FubarDev.FtpServer;

using JetBrains.Annotations;

using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// Extension methods for <see cref="IHostBuilder"/>.
    /// </summary>
    public static class HostBuilderExtensions
    {
        /// <summary>
        /// Adds the FTP server to the host builder.
        /// </summary>
        /// <param name="builder">The host builder to add the FTP host service to.</param>
        /// <param name="configure">Configuration of the FTP server services.</param>
        /// <returns>The host builder.</returns>
        public static IHostBuilder AddFtpServer(
            this IHostBuilder builder,
            [NotNull] Action<IFtpServerBuilder> configure)
        {
            return builder.ConfigureServices(
                (_, services) => { services.AddFtpServer(configure); });
        }
    }
}
