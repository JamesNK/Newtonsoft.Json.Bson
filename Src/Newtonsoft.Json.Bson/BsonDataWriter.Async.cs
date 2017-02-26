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

using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Bson.Utilities;

namespace Newtonsoft.Json.Bson
{
    public partial class BsonDataWriter
    {
        private readonly bool _safeAsync;
        private bool _finishingAsync;

        /// <summary>
        /// Asynchronously flushes whatever is in the buffer to the destination and also flushes the destination.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
        /// <remarks>Because BSON documents are written as a single unit, only <see cref="FlushAsync"/>,
        /// <see cref="CloseAsync"/> and the final <see cref="WriteEndAsync(CancellationToken)"/>,
        /// <see cref="WriteEndArrayAsync"/> or <see cref="WriteEndObjectAsync"/>
        /// that finishes writing the document will write asynchronously. Derived classes will not write asynchronously.</remarks>
        public override Task FlushAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            return _safeAsync ? _writer.FlushAsync(cancellationToken) : base.FlushAsync(cancellationToken);
        }

        /// <summary>
        /// Asynchronously writes the end of the current JSON object or array.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
        /// <remarks>Because BSON documents are written as a single unit, only <see cref="FlushAsync"/>,
        /// <see cref="CloseAsync"/> and the final <see cref="WriteEndAsync(CancellationToken)"/>,
        /// <see cref="WriteEndArrayAsync"/> or <see cref="WriteEndObjectAsync"/>
        /// that finishes writing the document will write asynchronously. Derived classes will not write asynchronously.</remarks>
        public override Task WriteEndAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return _safeAsync && Top == 1 ? CompleteAsync(cancellationToken) : base.WriteEndAsync(cancellationToken);
        }

        private bool WillCloseAll(BsonType type)
        {
            if (type != _root.Type)
            {
                return false;
            }

            for (var token = _parent; token != _root; token = token.Parent)
            {
                if (token.Type == type) // Will only close as far as here.
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Asynchronously writes the end of an array.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
        /// <remarks>Because BSON documents are written as a single unit, only <see cref="FlushAsync"/>,
        /// <see cref="CloseAsync"/> and the final <see cref="WriteEndAsync(CancellationToken)"/>,
        /// <see cref="WriteEndArrayAsync"/> or <see cref="WriteEndObjectAsync"/>
        /// that finishes writing the document will write asynchronously. Derived classes will not write asynchronously.</remarks>
        public override Task WriteEndArrayAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            return _safeAsync && WillCloseAll(BsonType.Array) ? CompleteAsync(cancellationToken) : base.WriteEndArrayAsync(cancellationToken);
        }

        /// <summary>
        /// Asynchronously writes the end of a JSON object.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
        /// <remarks>Because BSON documents are written as a single unit, only <see cref="FlushAsync"/>,
        /// <see cref="CloseAsync"/> and the final <see cref="WriteEndAsync(CancellationToken)"/>,
        /// <see cref="WriteEndArrayAsync"/> or <see cref="WriteEndObjectAsync"/>
        /// that finishes writing the document will write asynchronously. Derived classes will not write asynchronously.</remarks>
        public override Task WriteEndObjectAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            return _safeAsync && WillCloseAll(BsonType.Object) ? CompleteAsync(cancellationToken) : base.WriteEndObjectAsync(cancellationToken);
        }

        /// <summary>
        /// Asynchronously closes this writer.
        /// If <see cref="JsonWriter.CloseOutput"/> is set to <c>true</c>, the destination is also closed.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
        /// <remarks>Because BSON documents are written as a single unit, only <see cref="FlushAsync"/>,
        /// <see cref="CloseAsync"/> and the final <see cref="WriteEndAsync(CancellationToken)"/>,
        /// <see cref="WriteEndArrayAsync"/> or <see cref="WriteEndObjectAsync"/>
        /// that finishes writing the document will write asynchronously. Derived classes will not write asynchronously.</remarks>
        public override Task CloseAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            if (_safeAsync)
            {
                if (AutoCompleteOnClose)
                {
                    if (CloseOutput)
                    {
                        return CompleteAndCloseOutputAsync(cancellationToken);
                    }

                    return CompleteAsync(cancellationToken);
                }

                if (CloseOutput)
                {
                    _writer?.Close();
                }

                return AsyncUtils.CompletedTask;
            }

            return base.CloseAsync(cancellationToken);
        }

        private Task CompleteAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return cancellationToken.FromCanceled();
            }

            int top = Top;
            if (top == 0)
            {
                return AsyncUtils.CompletedTask;
            }

            _finishingAsync = true;
            try
            {
                while (top-- != 0)
                {
                    WriteEnd();
                }
            }
            finally
            {
                _finishingAsync = false;
            }

            return _writer.WriteTokenAsync(_root, cancellationToken);
        }

        private async Task CompleteAndCloseOutputAsync(CancellationToken cancellationToken)
        {
            await CompleteAsync(cancellationToken).ConfigureAwait(false);
            _writer?.Close();
        }
    }
}

#endif
