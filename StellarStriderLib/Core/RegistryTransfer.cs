using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace StellarStriderLib.Core
{
    /// <summary>Splits registry manifests into bounded chunks and verifies their SHA-256 digests.</summary>
    internal static class RegistryTransfer
    {
        internal const int MaximumBytes = 4 * 1024 * 1024;

        internal static string Digest(byte[] data)
        {
            using (var hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "");
            }
        }

        internal static byte[][] Split(byte[] data, int size)
        {
            if (data.Length == 0 || data.Length > MaximumBytes || size < 128)
            {
                throw new InvalidDataException("Registry transfer size is invalid.");
            }

            return Enumerable.Range(0, (data.Length + size - 1) / size)
                .Select(i => data.Skip(i * size).Take(size).ToArray()).ToArray();
        }

        /// <summary>Reassembles one registry transfer while checking chunk consistency and the final digest.</summary>
        internal sealed class Receiver
        {
            private string _token, _digest;
            private byte[][] _chunks;
            private int _bytes, _received;

            internal byte[] Add(string token, string digest, int index, int count, byte[] data)
            {
                if (token == null || token.Length != 32 || digest == null || digest.Length != 64 || count < 1 ||
                    count > 32768 || index < 0 || index >= count || data == null || data.Length < 1 ||
                    data.Length > 8192)
                {
                    throw new InvalidDataException("Invalid registry chunk.");
                }

                if (_token == null)
                {
                    _token = token;
                    _digest = digest;
                    _chunks = new byte[count][];
                }

                if (_token != token || _digest != digest || _chunks.Length != count)
                {
                    throw new InvalidDataException("Registry transfer changed during handshake.");
                }

                if (_chunks[index] != null)
                {
                    if (!_chunks[index].SequenceEqual(data))
                    {
                        throw new InvalidDataException("Conflicting registry chunk.");
                    }

                    return null;
                }

                _bytes += data.Length;
                if (_bytes > MaximumBytes)
                {
                    throw new InvalidDataException("Registry exceeds transfer size limit.");
                }

                _chunks[index] = data;
                if (++_received != count)
                {
                    return null;
                }

                var bytes = _chunks.SelectMany(c => c).ToArray();
                if (Digest(bytes) != digest)
                {
                    throw new InvalidDataException("Registry checksum mismatch.");
                }

                return bytes;
            }
        }
    }
}
