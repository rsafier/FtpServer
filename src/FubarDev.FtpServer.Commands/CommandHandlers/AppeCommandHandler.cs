//-----------------------------------------------------------------------
// <copyright file="AppeCommandHandler.cs" company="Fubar Development Junker">
//     Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>
// <author>Mark Junker</author>
//-----------------------------------------------------------------------

using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.BackgroundTransfer;
using FubarDev.FtpServer.FileSystem;

using JetBrains.Annotations;

namespace FubarDev.FtpServer.CommandHandlers
{
    /// <summary>
    /// Implements the <code>APPE</code> command.
    /// </summary>
    public class AppeCommandHandler : FtpCommandHandler
    {
        [NotNull]
        private readonly IFtpServer _server;

        /// <summary>
        /// Initializes a new instance of the <see cref="AppeCommandHandler"/> class.
        /// </summary>
        /// <param name="connection">The connection to create this command handler for.</param>
        /// <param name="server">The FTP server.</param>
        public AppeCommandHandler([NotNull] IFtpConnection connection, [NotNull] IFtpServer server)
            : base(connection, "APPE")
        {
            _server = server;
        }

        /// <inheritdoc/>
        public override bool IsAbortable => true;

        /// <inheritdoc/>
        public override async Task<FtpResponse> Process(FtpCommand command, CancellationToken cancellationToken)
        {
            var restartPosition = Data.RestartPosition;
            Data.RestartPosition = null;

            if (!Data.TransferMode.IsBinary && Data.TransferMode.FileType != FtpFileType.Ascii)
            {
                throw new NotSupportedException();
            }

            var fileName = command.Argument;
            if (string.IsNullOrEmpty(fileName))
            {
                return new FtpResponse(501, "No file name specified");
            }

            var currentPath = Data.Path.Clone();
            var fileInfo = await Data.FileSystem.SearchFileAsync(currentPath, fileName, cancellationToken).ConfigureAwait(false);
            if (fileInfo == null)
            {
                return new FtpResponse(550, "Not a valid directory.");
            }

            if (fileInfo.FileName == null)
            {
                return new FtpResponse(553, "File name not allowed.");
            }

            await Connection.WriteAsync(new FtpResponse(150, "Opening connection for data transfer."), cancellationToken).ConfigureAwait(false);

            return await Connection
                .SendResponseAsync(client => ExecuteSend(client, fileInfo, restartPosition, cancellationToken))
                .ConfigureAwait(false);
        }

        private async Task<FtpResponse> ExecuteSend(
            TcpClient responseSocket,
            [NotNull] SearchResult<IUnixFileEntry> fileInfo,
            long? restartPosition,
            CancellationToken cancellationToken)
        {
            var responseStream = responseSocket.GetStream();
            responseStream.ReadTimeout = 10000;

            using (var stream = await Connection.CreateEncryptedStream(responseStream).ConfigureAwait(false))
            {
                IBackgroundTransfer backgroundTransfer;
                if ((restartPosition != null && restartPosition.Value == 0) || fileInfo.Entry == null)
                {
                    if (fileInfo.Entry == null)
                    {
                        var fileName = fileInfo.FileName ?? throw new InvalidOperationException();
                        backgroundTransfer = await Data.FileSystem
                            .CreateAsync(fileInfo.Directory, fileName, stream, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        backgroundTransfer = await Data.FileSystem
                            .ReplaceAsync(fileInfo.Entry, stream, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    backgroundTransfer = await Data.FileSystem
                        .AppendAsync(fileInfo.Entry, restartPosition, stream, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (backgroundTransfer != null)
                {
                    _server.EnqueueBackgroundTransfer(backgroundTransfer, Connection);
                }
            }

            return new FtpResponse(226, "Uploaded file successfully.");
        }
    }
}
