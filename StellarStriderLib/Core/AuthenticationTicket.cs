using System;

namespace StellarStriderLib.Core
{
    internal enum Acknowledgement
    {
        Ignored,
        Accepted,
        Rejected
    }

    /// <summary>Accepts one acknowledgement matching the completed registry transfer token and digest.</summary>
    internal sealed class AuthenticationTicket
    {
        internal string Token { get; } = Guid.NewGuid().ToString("N");

        internal string Digest { get; private set; }

        private bool _sent, _finished;

        internal void SetManifest(byte[] bytes)
        {
            if (Digest != null)
            {
                throw new InvalidOperationException("Authentication manifest is immutable.");
            }

            Digest = RegistryTransfer.Digest(bytes);
        }

        internal void MarkSent()
        {
            if (Digest == null)
            {
                throw new InvalidOperationException("No authentication manifest.");
            }

            _sent = true;
        }

        internal Acknowledgement Acknowledge(string token, string digest, string error)
        {
            if (_finished || !_sent || token != Token || digest != Digest)
            {
                return Acknowledgement.Ignored;
            }

            _finished = true;
            return string.IsNullOrEmpty(error) ? Acknowledgement.Accepted : Acknowledgement.Rejected;
        }
    }
}
