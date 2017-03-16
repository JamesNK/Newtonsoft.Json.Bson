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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
#if HAVE_BIG_INTEGER
using System.Numerics;
#endif
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Bson.Utilities;

namespace Newtonsoft.Json.Bson
{
    public partial class BsonDataReader
    {
        private readonly AsyncBinaryReader _asyncReader;

        private Task<string> ReadElementAsync(CancellationToken cancellationToken)
        {
            Task<BsonType> typeReadTask = ReadTypeAsync(cancellationToken);
            if (typeReadTask.Status == TaskStatus.RanToCompletion)
            {
                _currentElementType = typeReadTask.Result;
                return ReadStringAsync(cancellationToken);
            }
            
            return ReadElementAsync(typeReadTask, cancellationToken);
        }

        private async Task<string> ReadElementAsync(Task<BsonType> typeReadTask, CancellationToken cancellationToken)
        {
            _currentElementType = await typeReadTask.ConfigureAwait(false);
            return await ReadStringAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously reads the next JSON token from the source.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous read. The <see cref="P:System.Threading.Tasks.Task`1.Result" />
        /// property returns <c>true</c> if the next token was read successfully; <c>false</c> if there are no more tokens to read.</returns>
        /// <remarks>Derived classes must override this method to get asynchronous behaviour. Otherwise it will
        /// execute synchronously, returning an already-completed task. Asynchronous behaviour is also not available when the
        /// constructor was passed an instance of type derived from <see cref="BinaryReader"/>.</remarks>
        public override Task<bool> ReadAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            Task<bool> readTask;
            switch (_bsonReaderState)
            {
                case BsonReaderState.Normal:
                    readTask = ReadNormalAsync(cancellationToken);
                    break;
                case BsonReaderState.ReferenceStart:
                case BsonReaderState.ReferenceRef:
                case BsonReaderState.ReferenceId:
                    readTask = ReadReferenceAsync(cancellationToken);
                    break;
                case BsonReaderState.CodeWScopeStart:
                case BsonReaderState.CodeWScopeCode:
                case BsonReaderState.CodeWScopeScope:
                case BsonReaderState.CodeWScopeScopeObject:
                case BsonReaderState.CodeWScopeScopeEnd:
                    readTask = ReadCodeWScopeAsync(cancellationToken);
                    break;
                default:
                    throw ExceptionUtils.CreateJsonReaderException(this, "Unexpected state: {0}".FormatWith(CultureInfo.InvariantCulture, _bsonReaderState));
            }

            if (readTask.Status == TaskStatus.RanToCompletion)
            {
                if (!readTask.Result)
                {
                    SetToken(JsonToken.None);
                }

                return readTask;
            }

            return ReadCatchingEndOfStreamAsync(readTask);
        }

        private async Task<bool> ReadCatchingEndOfStreamAsync(Task<bool> task)
        {
            try
            {
                if (await task.ConfigureAwait(false))
                {
                    return true;
                }
            }
            catch (EndOfStreamException)
            {
                // Eat and set token none below.
            }

            SetToken(JsonToken.None);
            return false;
        }

        private async Task<bool> ReadCodeWScopeCodeAsync(CancellationToken cancellationToken)
        {
            // total CodeWScope size - not used
            await ReadInt32Async(cancellationToken).ConfigureAwait(false);

            SetToken(JsonToken.String, await ReadLengthStringAsync(cancellationToken).ConfigureAwait(false));
            _bsonReaderState = BsonReaderState.CodeWScopeScope;
            return true;
        }

        private async Task<bool> ReadCodeWScopeScopeAsync(CancellationToken cancellationToken)
        {
            SetToken(JsonToken.StartObject);
            _bsonReaderState = BsonReaderState.CodeWScopeScopeObject;

            ContainerContext newContext = new ContainerContext(BsonType.Object);
            PushContext(newContext);
            newContext.Length = await ReadInt32Async(cancellationToken).ConfigureAwait(false);

            return true;
        }

        private async Task<bool> ReadCodeWScopeScopeObjectAsync(CancellationToken cancellationToken)
        {
            bool result = await ReadNormalAsync(cancellationToken).ConfigureAwait(false);
            if (result && TokenType == JsonToken.EndObject)
            {
                _bsonReaderState = BsonReaderState.CodeWScopeScopeEnd;
            }

            return result;
        }

        private Task<bool> ReadCodeWScopeAsync(CancellationToken cancellationToken)
        {
            switch (_bsonReaderState)
            {
                case BsonReaderState.CodeWScopeStart:
                case BsonReaderState.CodeWScopeScopeEnd:
                    break;
                case BsonReaderState.CodeWScopeCode:
                    return ReadCodeWScopeCodeAsync(cancellationToken);
                case BsonReaderState.CodeWScopeScope:
                    if (CurrentState != State.PostValue)
                    {
                        return ReadCodeWScopeScopeAsync(cancellationToken);
                    }

                    break;
                case BsonReaderState.CodeWScopeScopeObject:
                    return ReadCodeWScopeScopeObjectAsync(cancellationToken);
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Handle synchronously.
            ReadCodeWScope();
            return AsyncUtils.True;
        }

        private async Task<bool> ReadPropertyReferenceAsync(CancellationToken cancellationToken)
        {
            switch (_bsonReaderState)
            {
                case BsonReaderState.ReferenceRef:
                    SetToken(JsonToken.String, await ReadLengthStringAsync(cancellationToken).ConfigureAwait(false));
                    return true;
                case BsonReaderState.ReferenceId:
                    SetToken(JsonToken.Bytes, await ReadBytesAsync(12, cancellationToken).ConfigureAwait(false));
                    return true;
                default:
                    throw ExceptionUtils.CreateJsonReaderException(this, "Unexpected state when reading BSON reference: " + _bsonReaderState);
            }
        }

        private Task<bool> ReadReferenceAsync(CancellationToken cancellationToken)
        {
            if (CurrentState == State.Property)
            {
                return ReadPropertyReferenceAsync(cancellationToken);
            }

            // Handle synchronously.
            ReadReference();
            return AsyncUtils.True;
        }

        private async Task<bool> ReadNormalStartAsync(CancellationToken cancellationToken)
        {
            JsonToken token = (!_readRootValueAsArray) ? JsonToken.StartObject : JsonToken.StartArray;
            BsonType type = (!_readRootValueAsArray) ? BsonType.Object : BsonType.Array;

            SetToken(token);
            ContainerContext newContext = new ContainerContext(type);
            PushContext(newContext);
            newContext.Length = await ReadInt32Async(cancellationToken).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> ReadNormalPropertyAsync(CancellationToken cancellationToken)
        {
            await ReadTypeAsync(_currentElementType, cancellationToken).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> ReadNormalPostValueAsync(ContainerContext context, CancellationToken cancellationToken)
        {
            int lengthMinusEnd = context.Length - 1;

            if (context.Position < lengthMinusEnd)
            {
                if (context.Type == BsonType.Array)
                {
                    await ReadElementAsync(cancellationToken).ConfigureAwait(false);
                    await ReadTypeAsync(_currentElementType, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    SetToken(JsonToken.PropertyName, await ReadElementAsync(cancellationToken).ConfigureAwait(false));
                }
            }
            else if (context.Position == lengthMinusEnd)
            {
                if (await ReadByteAsync(cancellationToken).ConfigureAwait(false) != 0)
                {
                    throw ExceptionUtils.CreateJsonReaderException(this, "Unexpected end of object byte value.");
                }

                PopContext();
                if (_currentContext != null)
                {
                    MovePosition(context.Length);
                }

                SetToken(context.Type == BsonType.Object ? JsonToken.EndObject : JsonToken.EndArray);
            }
            else
            {
                throw ExceptionUtils.CreateJsonReaderException(this, "Read past end of current container context.");
            }

            return true;
        }

        private Task<bool> ReadNormalAsync(CancellationToken cancellationToken)
        {
            switch (CurrentState)
            {
                case State.Start:
                    return ReadNormalStartAsync(cancellationToken);
                case State.Property:
                    return ReadNormalPropertyAsync(cancellationToken);
                case State.ObjectStart:
                case State.ArrayStart:
                case State.PostValue:
                    ContainerContext context = _currentContext;
                    return context == null ? AsyncUtils.False : ReadNormalPostValueAsync(context, cancellationToken);
                case State.Complete:
                case State.Closed:
                case State.ConstructorStart:
                case State.Constructor:
                case State.Error:
                case State.Finished:
                    return AsyncUtils.False;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private Task<byte> ReadByteAsync(CancellationToken cancellationToken)
        {
            MovePosition(1);
            return _asyncReader.ReadByteAsync(cancellationToken);
        }

        private Task ReadTypeAsync(BsonType type, CancellationToken cancellationToken)
        {
            switch (type)
            {
                case BsonType.Number:
                case BsonType.String:
                case BsonType.Symbol:
                case BsonType.Object:
                case BsonType.Array:
                case BsonType.Binary:
                case BsonType.Oid:
                case BsonType.Boolean:
                case BsonType.Date:
                case BsonType.Regex:
                case BsonType.Code:
                case BsonType.Integer:
                case BsonType.TimeStamp:
                case BsonType.Long:
                    return ReadTypeTrulyAsync(type, cancellationToken);
                case BsonType.Undefined:
                case BsonType.Null:
                case BsonType.Reference:
                case BsonType.CodeWScope:
                    ReadType(type);
                    return AsyncUtils.CompletedTask;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), "Unexpected BsonType value: " + type);
            }
        }

        private async Task ReadTypeTrulyAsync(BsonType type, CancellationToken cancellationToken)
        {
            switch (type)
            {
                case BsonType.Number:

                    if (FloatParseHandling == FloatParseHandling.Decimal)
                    {
                        SetToken(JsonToken.Float, Convert.ToDecimal(await ReadDoubleAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        SetToken(JsonToken.Float, await ReadDoubleAsync(cancellationToken).ConfigureAwait(false));
                    }
                    break;
                case BsonType.String:
                case BsonType.Symbol:
                    SetToken(JsonToken.String, await ReadLengthStringAsync(cancellationToken).ConfigureAwait(false));
                    break;
                case BsonType.Object:
                {
                    SetToken(JsonToken.StartObject);

                    ContainerContext newContext = new ContainerContext(BsonType.Object);
                    PushContext(newContext);
                    newContext.Length = await ReadInt32Async(cancellationToken).ConfigureAwait(false);
                }
                    break;
                case BsonType.Array:
                {
                    SetToken(JsonToken.StartArray);

                    ContainerContext newContext = new ContainerContext(BsonType.Array);
                    PushContext(newContext);
                    newContext.Length = await ReadInt32Async(cancellationToken).ConfigureAwait(false);
                }
                    break;
                case BsonType.Binary:
                    Tuple<byte[], BsonBinaryType> data = await ReadBinaryAsync(cancellationToken).ConfigureAwait(false);

                    SetToken(JsonToken.Bytes, data.Item2 != BsonBinaryType.Uuid
                        ? data.Item1
                        : (object)new Guid(data.Item1));
                    break;
                case BsonType.Oid:
                    SetToken(JsonToken.Bytes, await ReadBytesAsync(12, cancellationToken).ConfigureAwait(false));
                    break;
                case BsonType.Boolean:
                    SetToken(JsonToken.Boolean, Convert.ToBoolean(await ReadByteAsync(cancellationToken).ConfigureAwait(false)));
                    break;
                case BsonType.Date:
                    DateTime dateTime = DateTimeUtils.ConvertJavaScriptTicksToDateTime(await ReadInt64Async(cancellationToken).ConfigureAwait(false));

                    switch (DateTimeKindHandling)
                    {
                        case DateTimeKind.Unspecified:
                            dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
                            break;
                        case DateTimeKind.Local:
                            dateTime = dateTime.ToLocalTime();
                            break;
                    }

                    SetToken(JsonToken.Date, dateTime);
                    break;
                case BsonType.Regex:
                    string expression = await ReadStringAsync(cancellationToken).ConfigureAwait(false);
                    string modifiers = await ReadStringAsync(cancellationToken).ConfigureAwait(false);

                    SetToken(JsonToken.String, @"/" + expression + @"/" + modifiers);
                    break;
                case BsonType.Code:
                    SetToken(JsonToken.String, await ReadLengthStringAsync(cancellationToken).ConfigureAwait(false));
                    break;
                case BsonType.Integer:
                    SetToken(JsonToken.Integer, (long)await ReadInt32Async(cancellationToken).ConfigureAwait(false));
                    break;
                case BsonType.TimeStamp:
                case BsonType.Long:
                    SetToken(JsonToken.Integer, await ReadInt64Async(cancellationToken).ConfigureAwait(false));
                    break;
            }
        }

        private async Task<Tuple<byte[], BsonBinaryType>> ReadBinaryAsync(CancellationToken cancellationToken)
        {
            int dataLength = await ReadInt32Async(cancellationToken).ConfigureAwait(false);

            BsonBinaryType binaryType = (BsonBinaryType)await ReadByteAsync(cancellationToken).ConfigureAwait(false);

#pragma warning disable 612,618
            // the old binary type has the data length repeated in the data for some reason
            if (binaryType == BsonBinaryType.BinaryOld && !_jsonNet35BinaryCompatibility)
            {
                dataLength = await ReadInt32Async(cancellationToken).ConfigureAwait(false);
            }
#pragma warning restore 612,618

            return Tuple.Create(await ReadBytesAsync(dataLength, cancellationToken).ConfigureAwait(false), binaryType);
        }

        private async Task<string> ReadStringAsync(CancellationToken cancellationToken)
        {
            EnsureBuffers();

            StringBuilder builder = null;

            int totalBytesRead = 0;
            // used in case of left over multibyte characters in the buffer
            int offset = 0;
            while (true)
            {
                int count = offset;
                byte b;
                while (count < MaxCharBytesSize && (b = await _asyncReader.ReadByteAsync(cancellationToken).ConfigureAwait(false)) > 0)
                {
                    _byteBuffer[count++] = b;
                }
                int byteCount = count - offset;
                totalBytesRead += byteCount;

                if (count < MaxCharBytesSize && builder == null)
                {
                    // pref optimization to avoid reading into a string builder
                    // if string is smaller than the buffer then return it directly
                    int length = Encoding.UTF8.GetChars(_byteBuffer, 0, byteCount, _charBuffer, 0);

                    MovePosition(totalBytesRead + 1);
                    return new string(_charBuffer, 0, length);
                }
                else
                {
                    // calculate the index of the end of the last full character in the buffer
                    int lastFullCharStop = GetLastFullCharStop(count - 1);

                    int charCount = Encoding.UTF8.GetChars(_byteBuffer, 0, lastFullCharStop + 1, _charBuffer, 0);

                    if (builder == null)
                    {
                        builder = new StringBuilder(MaxCharBytesSize * 2);
                    }

                    builder.Append(_charBuffer, 0, charCount);

                    if (lastFullCharStop < byteCount - 1)
                    {
                        offset = byteCount - lastFullCharStop - 1;
                        // copy left over multi byte characters to beginning of buffer for next iteration
                        Array.Copy(_byteBuffer, lastFullCharStop + 1, _byteBuffer, 0, offset);
                    }
                    else
                    {
                        // reached end of string
                        if (count < MaxCharBytesSize)
                        {
                            MovePosition(totalBytesRead + 1);
                            return builder.ToString();
                        }

                        offset = 0;
                    }
                }
            }
        }

        private async Task<string> ReadLengthStringAsync(CancellationToken cancellationToken)
        {
            int length = await ReadInt32Async(cancellationToken).ConfigureAwait(false);

            MovePosition(length);

            string s = await GetStringAsync(length - 1, cancellationToken).ConfigureAwait(false);
            await _asyncReader.ReadByteAsync(cancellationToken).ConfigureAwait(false);

            return s;
        }

        private Task<string> GetStringAsync(int length, CancellationToken cancellationToken)
        {
            return length == 0 ? AsyncUtils.EmptyString : GetNonEmptyStringAsync(length, cancellationToken);
        }

        private async Task<string> GetNonEmptyStringAsync(int length, CancellationToken cancellationToken)
        {
            EnsureBuffers();

            StringBuilder builder = null;

            int totalBytesRead = 0;

            // used in case of left over multibyte characters in the buffer
            int offset = 0;
            do
            {
                int count = length - totalBytesRead > MaxCharBytesSize - offset
                    ? MaxCharBytesSize - offset
                    : length - totalBytesRead;

                int byteCount = await _asyncReader.ReadAsync(_byteBuffer, offset, count, cancellationToken).ConfigureAwait(false);

                if (byteCount == 0)
                {
                    throw new EndOfStreamException("Unable to read beyond the end of the stream.");
                }

                totalBytesRead += byteCount;

                // Above, byteCount is how many bytes we read this time.
                // Below, byteCount is how many bytes are in the _byteBuffer.
                byteCount += offset;

                if (byteCount == length)
                {
                    // pref optimization to avoid reading into a string builder
                    // first iteration and all bytes read then return string directly
                    int charCount = Encoding.UTF8.GetChars(_byteBuffer, 0, byteCount, _charBuffer, 0);
                    return new string(_charBuffer, 0, charCount);
                }
                else
                {
                    int lastFullCharStop = GetLastFullCharStop(byteCount - 1);

                    if (builder == null)
                    {
                        builder = new StringBuilder(length);
                    }

                    int charCount = Encoding.UTF8.GetChars(_byteBuffer, 0, lastFullCharStop + 1, _charBuffer, 0);
                    builder.Append(_charBuffer, 0, charCount);

                    if (lastFullCharStop < byteCount - 1)
                    {
                        offset = byteCount - lastFullCharStop - 1;
                        // copy left over multi byte characters to beginning of buffer for next iteration
                        Array.Copy(_byteBuffer, lastFullCharStop + 1, _byteBuffer, 0, offset);
                    }
                    else
                    {
                        offset = 0;
                    }
                }
            } while (totalBytesRead < length);

            return builder.ToString();
        }

        private Task<double> ReadDoubleAsync(CancellationToken cancellationToken)
        {
            MovePosition(8);
            return _asyncReader.ReadDoubleAsync(cancellationToken);
        }

        private Task<int> ReadInt32Async(CancellationToken cancellationToken)
        {
            MovePosition(4);
            return _asyncReader.ReadInt32Async(cancellationToken);
        }

        private Task<long> ReadInt64Async(CancellationToken cancellationToken)
        {
            MovePosition(8);
            return _asyncReader.ReadInt64Async(cancellationToken);
        }

        private async Task<BsonType> ReadTypeAsync(CancellationToken cancellationToken)
        {
            MovePosition(1);
            return (BsonType)(sbyte)await _asyncReader.ReadByteAsync(cancellationToken).ConfigureAwait(false);
        }

        private Task<byte[]> ReadBytesAsync(int count, CancellationToken cancellationToken)
        {
            MovePosition(count);
            return _asyncReader.ReadBytesAsync(count, cancellationToken);
        }

        private async Task<JsonToken> GetContentTokenAsync(CancellationToken cancellationToken)
        {
            while (await ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                JsonToken t = TokenType;
                if (t != JsonToken.Comment)
                {
                    return t;
                }
            }

            SetToken(JsonToken.None);
            return JsonToken.None;
        }

        /// <summary>
        /// Asynchronously reads the next JSON token from the source as a <see cref="T:System.Nullable`1" /> of <see cref="T:System.Boolean" />.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous read. The <see cref="P:System.Threading.Tasks.Task`1.Result" />
        /// property returns the <see cref="T:System.Nullable`1" /> of <see cref="T:System.Boolean" />. This result will be <c>null</c> at the end of an array.</returns>
        /// <remarks>Derived classes must override this method to get asynchronous behaviour. Otherwise it will
        /// execute synchronously, returning an already-completed task. Asynchronous behaviour is also not available when the
        /// constructor was passed an instance of type derived from <see cref="BinaryReader"/>.</remarks>
        public override Task<bool?> ReadAsBooleanAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return _asyncReader == null ? base.ReadAsBooleanAsync(cancellationToken) : DoReadAsBooleanAsync(cancellationToken);
        }

        private async Task<bool?> DoReadAsBooleanAsync(CancellationToken cancellationToken)
        {
            JsonToken t = await GetContentTokenAsync(cancellationToken).ConfigureAwait(false);

            switch (t)
            {
                case JsonToken.None:
                case JsonToken.Null:
                case JsonToken.EndArray:
                    return null;
                case JsonToken.Integer:
                case JsonToken.Float:
                    bool b;
#if HAVE_BIG_INTEGER
                    if (Value is BigInteger)
                    {
                        b = (BigInteger)Value != 0;
                    }
                    else
#endif
                    {
                        b = Convert.ToBoolean(Value, CultureInfo.InvariantCulture);
                    }

                    SetToken(JsonToken.Boolean, b, false);

                    return b;
                case JsonToken.String:
                    return ReadBooleanString((string)Value);
                case JsonToken.Boolean:
                    return (bool)Value;
            }

            throw ExceptionUtils.CreateJsonReaderException(this, "Error reading boolean. Unexpected token: {0}.".FormatWith(CultureInfo.InvariantCulture, t));
        }

        /// <summary>
        /// Asynchronously reads the next JSON token from the source as a <see cref="T:System.Byte" />[].
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous read. The <see cref="P:System.Threading.Tasks.Task`1.Result" />
        /// property returns the <see cref="T:System.Byte" />[]. This result will be <c>null</c> at the end of an array.</returns>
        /// <remarks>Derived classes must override this method to get asynchronous behaviour. Otherwise it will
        /// execute synchronously, returning an already-completed task. Asynchronous behaviour is also not available when the
        /// constructor was passed an instance of type derived from <see cref="BinaryReader"/>.</remarks>
        public override Task<byte[]> ReadAsBytesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return _asyncReader == null ? base.ReadAsBytesAsync(cancellationToken) : DoReadAsBytesAsync(cancellationToken);
        }

        private async Task<byte[]> DoReadAsBytesAsync(CancellationToken cancellationToken)
        {
            JsonToken t = await GetContentTokenAsync(cancellationToken).ConfigureAwait(false);

            switch (t)
            {
                case JsonToken.StartObject:
                    {
                        await ReadIntoWrappedTypeObjectAsync(cancellationToken).ConfigureAwait(false);

                        byte[] data = await ReadAsBytesAsync(cancellationToken).ConfigureAwait(false);
                        await ReaderReadAndAssertAsync(cancellationToken).ConfigureAwait(false);

                        if (TokenType != JsonToken.EndObject)
                        {
                            throw ExceptionUtils.CreateJsonReaderException(this, "Error reading bytes. Unexpected token: {0}.".FormatWith(CultureInfo.InvariantCulture, TokenType));
                        }

                        SetToken(JsonToken.Bytes, data, false);
                        return data;
                    }
                case JsonToken.String:
                    {
                        // attempt to convert possible base 64 or GUID string to bytes
                        // GUID has to have format 00000000-0000-0000-0000-000000000000
                        string s = (string)Value;

                        byte[] data;

                        Guid g;
                        if (s.Length == 0)
                        {
                            data = CollectionUtils.ArrayEmpty<byte>();
                        }
                        else if (ConvertUtils.TryConvertGuid(s, out g))
                        {
                            data = g.ToByteArray();
                        }
                        else
                        {
                            data = Convert.FromBase64String(s);
                        }

                        SetToken(JsonToken.Bytes, data, false);
                        return data;
                    }
                case JsonToken.None:
                case JsonToken.Null:
                case JsonToken.EndArray:
                    return null;
                case JsonToken.Bytes:
                    if (ValueType == typeof(Guid))
                    {
                        byte[] data = ((Guid)Value).ToByteArray();
                        SetToken(JsonToken.Bytes, data, false);
                        return data;
                    }

                    return (byte[])Value;
                case JsonToken.StartArray:
                    return await ReadArrayIntoByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }

            throw ExceptionUtils.CreateJsonReaderException(this, "Error reading bytes. Unexpected token: {0}.".FormatWith(CultureInfo.InvariantCulture, t));
        }

        private async Task ReadIntoWrappedTypeObjectAsync(CancellationToken cancellationToken)
        {
            await ReaderReadAndAssertAsync(cancellationToken).ConfigureAwait(false);
            if (Value?.ToString() == "$type")
            {
                await ReaderReadAndAssertAsync(cancellationToken).ConfigureAwait(false);
                if (Value != null && Value.ToString().StartsWith("System.Byte[]", StringComparison.Ordinal))
                {
                    await ReaderReadAndAssertAsync(cancellationToken).ConfigureAwait(false);
                    if (Value?.ToString() == "$value")
                    {
                        return;
                    }
                }
            }

            throw ExceptionUtils.CreateJsonReaderException(this, "Error reading bytes. Unexpected token: {0}.".FormatWith(CultureInfo.InvariantCulture, JsonToken.StartObject));
        }

        /// <summary>
        /// Asynchronously reads the next JSON token from the source as a <see cref="T:System.Nullable`1" /> of <see cref="T:System.DateTime" />.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous read. The <see cref="P:System.Threading.Tasks.Task`1.Result" />
        /// property returns the <see cref="T:System.Nullable`1" /> of <see cref="T:System.DateTime" />. This result will be <c>null</c> at the end of an array.</returns>
        /// <remarks>Derived classes must override this method to get asynchronous behaviour. Otherwise it will
        /// execute synchronously, returning an already-completed task. Asynchronous behaviour is also not available when the
        /// constructor was passed an instance of type derived from <see cref="BinaryReader"/>.</remarks>
        public override Task<DateTime?> ReadAsDateTimeAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return _asyncReader == null ? base.ReadAsDateTimeAsync(cancellationToken) : DoReadAsDateTimeAsync(cancellationToken);
        }

        private async Task<DateTime?> DoReadAsDateTimeAsync(CancellationToken cancellationToken)
        {
            switch (await GetContentTokenAsync(cancellationToken).ConfigureAwait(false))
            {
                case JsonToken.None:
                case JsonToken.Null:
                case JsonToken.EndArray:
                    return null;
                case JsonToken.Date:
#if HAVE_DATE_TIME_OFFSET
                    if (Value is DateTimeOffset)
                    {
                        SetToken(JsonToken.Date, ((DateTimeOffset)Value).DateTime, false);
                    }
#endif

                    return (DateTime)Value;
                case JsonToken.String:
                    return ReadDateTimeString((string)Value);
            }

            throw ExceptionUtils.CreateJsonReaderException(this, "Error reading date. Unexpected token: {0}.".FormatWith(CultureInfo.InvariantCulture, TokenType));
        }

        /// <summary>
        /// Asynchronously reads the next JSON token from the source as a <see cref="T:System.Nullable`1" /> of <see cref="T:System.DateTimeOffset" />.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous read. The <see cref="P:System.Threading.Tasks.Task`1.Result" />
        /// property returns the <see cref="T:System.Nullable`1" /> of <see cref="T:System.DateTimeOffset" />. This result will be <c>null</c> at the end of an array.</returns>
        /// <remarks>Derived classes must override this method to get asynchronous behaviour. Otherwise it will
        /// execute synchronously, returning an already-completed task. Asynchronous behaviour is also not available when the
        /// constructor was passed an instance of type derived from <see cref="BinaryReader"/>.</remarks>
        public override Task<DateTimeOffset?> ReadAsDateTimeOffsetAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return _asyncReader == null ? base.ReadAsDateTimeOffsetAsync(cancellationToken) : DoReadAsDateTimeOffsetAsync(cancellationToken);
        }

        private async Task<DateTimeOffset?> DoReadAsDateTimeOffsetAsync(CancellationToken cancellationToken)
        {
            JsonToken t = await GetContentTokenAsync(cancellationToken).ConfigureAwait(false);

            switch (t)
            {
                case JsonToken.None:
                case JsonToken.Null:
                case JsonToken.EndArray:
                    return null;
                case JsonToken.Date:
                    if (Value is DateTime)
                    {
                        SetToken(JsonToken.Date, new DateTimeOffset((DateTime)Value), false);
                    }

                    return (DateTimeOffset)Value;
                case JsonToken.String:
                    return ReadDateTimeOffsetString((string)Value);
                default:
                    throw ExceptionUtils.CreateJsonReaderException(this, "Error reading date. Unexpected token: {0}.".FormatWith(CultureInfo.InvariantCulture, t));
            }
        }

        internal DateTimeOffset? ReadDateTimeOffsetString(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                SetToken(JsonToken.Null, null, false);
                return null;
            }

            DateTimeOffset dt;
            if (DateTimeUtils.TryParseDateTimeOffset(s, DateFormatString, Culture, out dt))
            {
                SetToken(JsonToken.Date, dt, false);
                return dt;
            }

            if (DateTimeOffset.TryParse(s, Culture, DateTimeStyles.RoundtripKind, out dt))
            {
                SetToken(JsonToken.Date, dt, false);
                return dt;
            }

            SetToken(JsonToken.String, s, false);
            throw ExceptionUtils.CreateJsonReaderException(this, "Could not convert string to DateTimeOffset: {0}.".FormatWith(CultureInfo.InvariantCulture, s));
        }

        /// <summary>
        /// Asynchronously reads the next JSON token from the source as a <see cref="T:System.Nullable`1" /> of <see cref="T:System.Decimal" />.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous read. The <see cref="P:System.Threading.Tasks.Task`1.Result" />
        /// property returns the <see cref="T:System.Nullable`1" /> of <see cref="T:System.Decimal" />. This result will be <c>null</c> at the end of an array.</returns>
        /// <remarks>Derived classes must override this method to get asynchronous behaviour. Otherwise it will
        /// execute synchronously, returning an already-completed task. Asynchronous behaviour is also not available when the
        /// constructor was passed an instance of type derived from <see cref="BinaryReader"/>.</remarks>
        public override Task<decimal?> ReadAsDecimalAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return _asyncReader == null ? base.ReadAsDecimalAsync(cancellationToken) : DoReadAsDecimalAsync(cancellationToken);
        }

        private async Task<decimal?> DoReadAsDecimalAsync(CancellationToken cancellationToken)
        {
            JsonToken t = await GetContentTokenAsync(cancellationToken).ConfigureAwait(false);

            switch (t)
            {
                case JsonToken.None:
                case JsonToken.Null:
                case JsonToken.EndArray:
                    return null;
                case JsonToken.Integer:
                case JsonToken.Float:
                    if (!(Value is decimal))
                    {
                        SetToken(JsonToken.Float, Convert.ToDecimal(Value, CultureInfo.InvariantCulture), false);
                    }

                    return (decimal)Value;
                case JsonToken.String:
                    return ReadDecimalString((string)Value);
            }

            throw ExceptionUtils.CreateJsonReaderException(this, "Error reading decimal. Unexpected token: {0}.".FormatWith(CultureInfo.InvariantCulture, t));
        }

        /// <summary>
        /// Asynchronously reads the next JSON token from the source as a <see cref="T:System.Nullable`1" /> of <see cref="T:System.Double" />.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous read. The <see cref="P:System.Threading.Tasks.Task`1.Result" />
        /// property returns the <see cref="T:System.Nullable`1" /> of <see cref="T:System.Double" />. This result will be <c>null</c> at the end of an array.</returns>
        /// <remarks>Derived classes must override this method to get asynchronous behaviour. Otherwise it will
        /// execute synchronously, returning an already-completed task. Asynchronous behaviour is also not available when the
        /// constructor was passed an instance of type derived from <see cref="BinaryReader"/>.</remarks>
        public override Task<double?> ReadAsDoubleAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return _asyncReader == null ? base.ReadAsDoubleAsync(cancellationToken) : DoReadAsDoubleAsync(cancellationToken);
        }

        private async Task<double?> DoReadAsDoubleAsync(CancellationToken cancellationToken)
        {
            JsonToken t = await GetContentTokenAsync(cancellationToken).ConfigureAwait(false);

            switch (t)
            {
                case JsonToken.None:
                case JsonToken.Null:
                case JsonToken.EndArray:
                    return null;
                case JsonToken.Integer:
                case JsonToken.Float:
                    if (!(Value is double))
                    {
                        double d;
#if HAVE_BIG_INTEGER
                        if (Value is BigInteger)
                        {
                            d = (double)(BigInteger)Value;
                        }
                        else
#endif
                        {
                            d = Convert.ToDouble(Value, CultureInfo.InvariantCulture);
                        }

                        SetToken(JsonToken.Float, d, false);
                    }

                    return (double)Value;
                case JsonToken.String:
                    return ReadDoubleString((string)Value);
            }

            throw ExceptionUtils.CreateJsonReaderException(this, "Error reading double. Unexpected token: {0}.".FormatWith(CultureInfo.InvariantCulture, t));
        }

        /// <summary>
        /// Asynchronously reads the next JSON token from the source as a <see cref="T:System.Nullable`1" /> of <see cref="T:System.Int32" />.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous read. The <see cref="P:System.Threading.Tasks.Task`1.Result" />
        /// property returns the <see cref="T:System.Nullable`1" /> of <see cref="T:System.Int32" />. This result will be <c>null</c> at the end of an array.</returns>
        /// <remarks>Derived classes must override this method to get asynchronous behaviour. Otherwise it will
        /// execute synchronously, returning an already-completed task. Asynchronous behaviour is also not available when the
        /// constructor was passed an instance of type derived from <see cref="BinaryReader"/>.</remarks>
        public override Task<int?> ReadAsInt32Async(CancellationToken cancellationToken = default(CancellationToken))
        {
            return _asyncReader == null ? base.ReadAsInt32Async(cancellationToken) : DoReadAsInt32Async(cancellationToken);
        }

        private async Task<int?> DoReadAsInt32Async(CancellationToken cancellationToken)
        {
            JsonToken t = await GetContentTokenAsync(cancellationToken).ConfigureAwait(false);

            switch (t)
            {
                case JsonToken.None:
                case JsonToken.Null:
                case JsonToken.EndArray:
                    return null;
                case JsonToken.Integer:
                case JsonToken.Float:
                    if (!(Value is int))
                    {
                        SetToken(JsonToken.Integer, Convert.ToInt32(Value, CultureInfo.InvariantCulture), false);
                    }

                    return (int)Value;
                case JsonToken.String:
                    string s = (string)Value;
                    return ReadInt32String(s);
            }

            throw ExceptionUtils.CreateJsonReaderException(this, "Error reading integer. Unexpected token: {0}.".FormatWith(CultureInfo.InvariantCulture, t));
        }

        /// <summary>
        /// Asynchronously reads the next JSON token from the source as a <see cref="T:System.String" />.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous read. The <see cref="P:System.Threading.Tasks.Task`1.Result" />
        /// property returns the <see cref="T:System.String" />. This result will be <c>null</c> at the end of an array.</returns>
        /// <remarks>Derived classes must override this method to get asynchronous behaviour. Otherwise it will
        /// execute synchronously, returning an already-completed task. Asynchronous behaviour is also not available when the
        /// constructor was passed an instance of type derived from <see cref="BinaryReader"/>.</remarks>
        public override Task<string> ReadAsStringAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return _asyncReader == null ? base.ReadAsStringAsync(cancellationToken) : DoReadAsStringAsync(cancellationToken);
        }

        private async Task<string> DoReadAsStringAsync(CancellationToken cancellationToken)
        {
            JsonToken t = await GetContentTokenAsync(cancellationToken).ConfigureAwait(false);

            switch (t)
            {
                case JsonToken.None:
                case JsonToken.Null:
                case JsonToken.EndArray:
                    return null;
                case JsonToken.String:
                    return (string)Value;
            }

            if (JsonTokenUtils.IsPrimitiveToken(t))
            {
                if (Value != null)
                {
                    string s;
                    IFormattable formattable = Value as IFormattable;
                    if (formattable != null)
                    {
                        s = formattable.ToString(null, Culture);
                    }
                    else
                    {
                        Uri uri = Value as Uri;
                        s = uri != null ? uri.OriginalString : Value.ToString();
                    }

                    SetToken(JsonToken.String, s, false);
                    return s;
                }
            }

            throw ExceptionUtils.CreateJsonReaderException(this, "Error reading string. Unexpected token: {0}.".FormatWith(CultureInfo.InvariantCulture, t));
        }

        internal async Task ReaderReadAndAssertAsync(CancellationToken cancellationToken)
        {
            if (!await ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw CreateUnexpectedEndException();
            }
        }

        internal JsonReaderException CreateUnexpectedEndException()
        {
            return ExceptionUtils.CreateJsonReaderException(this, "Unexpected end when reading JSON.");
        }

        internal async Task<byte[]> ReadArrayIntoByteArrayAsync(CancellationToken cancellationToken)
        {
            List<byte> buffer = new List<byte>();

            while (true)
            {
                if (!await ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    SetToken(JsonToken.None);
                }

                if (ReadArrayElementIntoByteArrayReportDone(buffer))
                {
                    byte[] d = buffer.ToArray();
                    SetToken(JsonToken.Bytes, d, false);
                    return d;
                }
            }
        }

        private bool ReadArrayElementIntoByteArrayReportDone(List<byte> buffer)
        {
            switch (TokenType)
            {
                case JsonToken.None:
                    throw ExceptionUtils.CreateJsonReaderException(this, "Unexpected end when reading bytes.");
                case JsonToken.Integer:
                    buffer.Add(Convert.ToByte(Value, CultureInfo.InvariantCulture));
                    return false;
                case JsonToken.EndArray:
                    return true;
                case JsonToken.Comment:
                    return false;
                default:
                    throw ExceptionUtils.CreateJsonReaderException(this, "Unexpected token when reading bytes: {0}.".FormatWith(CultureInfo.InvariantCulture, TokenType));
            }
        }

        internal int? ReadInt32String(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                SetToken(JsonToken.Null, null, false);
                return null;
            }

            int i;
            if (int.TryParse(s, NumberStyles.Integer, Culture, out i))
            {
                SetToken(JsonToken.Integer, i, false);
                return i;
            }
            else
            {
                SetToken(JsonToken.String, s, false);
                throw ExceptionUtils.CreateJsonReaderException(this, "Could not convert string to integer: {0}.".FormatWith(CultureInfo.InvariantCulture, s));
            }
        }

        internal double? ReadDoubleString(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                SetToken(JsonToken.Null, null, false);
                return null;
            }

            double d;
            if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, Culture, out d))
            {
                SetToken(JsonToken.Float, d, false);
                return d;
            }
            else
            {
                SetToken(JsonToken.String, s, false);
                throw ExceptionUtils.CreateJsonReaderException(this, "Could not convert string to double: {0}.".FormatWith(CultureInfo.InvariantCulture, s));
            }
        }

        internal decimal? ReadDecimalString(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                SetToken(JsonToken.Null, null, false);
                return null;
            }

            decimal d;
            if (decimal.TryParse(s, NumberStyles.Number, Culture, out d))
            {
                SetToken(JsonToken.Float, d, false);
                return d;
            }
            else
            {
                SetToken(JsonToken.String, s, false);
                throw ExceptionUtils.CreateJsonReaderException(this, "Could not convert string to decimal: {0}.".FormatWith(CultureInfo.InvariantCulture, s));
            }
        }

        internal DateTime? ReadDateTimeString(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                SetToken(JsonToken.Null, null, false);
                return null;
            }

            DateTime dt;
            if (DateTimeUtils.TryParseDateTime(s, DateTimeZoneHandling, DateFormatString, Culture, out dt))
            {
                dt = DateTimeUtils.EnsureDateTime(dt, DateTimeZoneHandling);
                SetToken(JsonToken.Date, dt, false);
                return dt;
            }

            if (DateTime.TryParse(s, Culture, DateTimeStyles.RoundtripKind, out dt))
            {
                dt = DateTimeUtils.EnsureDateTime(dt, DateTimeZoneHandling);
                SetToken(JsonToken.Date, dt, false);
                return dt;
            }

            throw ExceptionUtils.CreateJsonReaderException(this, "Could not convert string to DateTime: {0}.".FormatWith(CultureInfo.InvariantCulture, s));
        }

        internal bool? ReadBooleanString(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                SetToken(JsonToken.Null, null, false);
                return null;
            }

            bool b;
            if (bool.TryParse(s, out b))
            {
                SetToken(JsonToken.Boolean, b, false);
                return b;
            }
            else
            {
                SetToken(JsonToken.String, s, false);
                throw ExceptionUtils.CreateJsonReaderException(this, "Could not convert string to boolean: {0}.".FormatWith(CultureInfo.InvariantCulture, s));
            }
        }
    }
}

#endif