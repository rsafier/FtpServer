// <copyright file="IAccountInformation.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using FubarDev.FtpServer.AccountManagement;

using JetBrains.Annotations;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Information about the account associated to a connection
    /// </summary>
    public interface IAccountInformation
    {
        /// <summary>
        /// Gets the current user name.
        /// </summary>
        [CanBeNull]
        IFtpUser User { get; }

        /// <summary>
        /// Gets a value indicating whether the user with the <see cref="User"/>.
        /// is logged in.
        /// </summary>
        bool IsLoggedIn { get; }

        /// <summary>
        /// Gets the membership provider that was used to authenticate the user.
        /// </summary>
        [CanBeNull]
        IMembershipProvider AuthenticatedBy { get; }

        /// <summary>
        /// Gets the FTP connection this user was authenticated for.
        /// </summary>
        [NotNull]
        IFtpConnection AuthenticatedFor { get; }

        /// <summary>
        /// Gets a value indicating whether the current user is anonymous.
        /// </summary>
        bool IsAnonymous { get; }
    }
}
