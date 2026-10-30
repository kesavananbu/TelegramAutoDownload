using System;

namespace TelegramAutoDownload.Services
{
    public sealed class UpdateIntegrityException : Exception
    {
        public UpdateIntegrityException(string message, bool missingIntegrityMetadata = false)
            : base(message)
        {
            MissingIntegrityMetadata = missingIntegrityMetadata;
        }

        /// <summary>
        /// When true, the release did not publish a verifiable digest — auto-install must not proceed.
        /// </summary>
        public bool MissingIntegrityMetadata { get; }
    }
}
