#region License
// Copyright (c) 2017 James Newton-King
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
#endregion

#if HAVE_ASYNC

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Newtonsoft.Json.Bson
{
    internal class AsyncBinaryReader : BinaryReader
    {
        private byte[] _buffer;

        private byte[] Buffer => _buffer ?? (_buffer = new byte[16]);

        public AsyncBinaryReader(Stream input) : base(input)
        {
        }

        private void EndOfStream()
        {
            throw new EndOfStreamException("Unable to read beyond the end of the stream.");
        }

        private void FileNotOpen()
        {
            throw new ObjectDisposedException("Cannot access a closed file.");
        }

        private async Task<byte[]> ReadBufferAsync(int size, CancellationToken cancellationToken)
        {
            var buffer = Buffer;
            int offset = 0;
            Stream stream = BaseStream;
            if (stream == null)
            {
                FileNotOpen();
            }

            do
            {
                int read = await BaseStream.ReadAsync(buffer, offset, size, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    EndOfStream();
                }

                offset += read;
                size -= read;
            } while (size > 0);

            return buffer;
        }

        public async Task<byte> ReadByteAsync(CancellationToken cancellationToken)
        {
            return (await ReadBufferAsync(1, cancellationToken).ConfigureAwait(false))[0];
        }

        public async Task<BsonType> ReadBsonTypeAsync(CancellationToken cancellationToken)
        {
            return (BsonType)(await ReadBufferAsync(1, cancellationToken).ConfigureAwait(false))[0];
        }

        public async Task<long> ReadInt64Async(CancellationToken cancellationToken)
        {
            var buffer = await ReadBufferAsync(8, cancellationToken).ConfigureAwait(false);
            uint lo = (uint)(buffer[0] | buffer[1] << 8 | buffer[2] << 16 | buffer[3] << 24);
            uint hi = (uint)(buffer[4] | buffer[5] << 8 | buffer[6] << 16 | buffer[7] << 24);
            return (long)hi << 32 | lo;
        }

        public async Task<int> ReadInt32Async(CancellationToken cancellationToken)
        {
            var buffer = await ReadBufferAsync(4, cancellationToken).ConfigureAwait(false);
            return buffer[0] | buffer[1] << 8 | buffer[2] << 16 | buffer[3] << 24;
        }

        public async Task<double> ReadDoubleAsync(CancellationToken cancellationToken)
        {
            var buffer = await ReadBufferAsync(8, cancellationToken).ConfigureAwait(false);
            uint lo = (uint)(buffer[0] | buffer[1] << 8 | buffer[2] << 16 | buffer[3] << 24);
            uint hi = (uint)(buffer[4] | buffer[5] << 8 | buffer[6] << 16 | buffer[7] << 24);
            return BitConverter.Int64BitsToDouble((long)hi << 32 | lo);
        }

        public Task<int> ReadAsync(byte[] buffer, int index, int count, CancellationToken cancellationToken)
        {
            Stream stream = BaseStream;
            if (stream == null)
            {
                FileNotOpen();
            }

            return stream.ReadAsync(buffer, index, count, cancellationToken);
        }

        public async Task<byte[]> ReadBytesAsync(int count, CancellationToken cancellationToken)
        {
            Stream stream = BaseStream;
            if (stream == null)
            {
                FileNotOpen();
            }

            if (count == 0)
            {
                return new byte[0];
            }

            byte[] result = new byte[count];
            int numRead = 0;
            do
            {
                int n = await stream.ReadAsync(result, numRead, count, cancellationToken).ConfigureAwait(false);
                if (n == 0)
                    break;
                numRead += n;
                count -= n;
            } while (count > 0);

            if (numRead != result.Length)
            {
                byte[] copy = new byte[numRead];
                result.CopyTo(copy, 0);
                return copy;
            }

            return result;
        }
    }

    internal sealed class AsyncBinaryReaderOwningReader : AsyncBinaryReader
    {
        private readonly BinaryReader _reader;

        public AsyncBinaryReaderOwningReader(BinaryReader reader)
            : base(reader.BaseStream)
        {
            Debug.Assert(reader.GetType() == typeof(BinaryReader));
            _reader = reader;
        }

#if HAVE_STREAM_READER_WRITER_CLOSE
        public override void Close()
        {
            // Don't call base.Close(). Let this reader decide
            // whether or not to close the stream.
            _reader.Close();
        }
#endif

        protected override void Dispose(bool disposing)
        {
            // Don't call base.Close(). Let this reader decide
            // whether or not to close the stream.
            ((IDisposable)_reader).Dispose();
        }
    }
}

#endif