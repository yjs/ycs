// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Ycs
{
    internal class DSDecoderV2 : IDSDecoder
    {
        private readonly bool _leaveOpen;
        private long _dsCurVal;

        public DSDecoderV2(Stream input, bool leaveOpen = false)
        {
            _leaveOpen = leaveOpen;
            Reader = input;
        }

        public Stream Reader { get; private set; }
        protected bool Disposed { get; private set; }

        public void Dispose()
        {
            Dispose(disposing: true);
            System.GC.SuppressFinalize(this);
        }

        public void ResetDsCurVal()
        {
            _dsCurVal = 0;
        }

        public long ReadDsClock()
        {
            _dsCurVal += Reader.ReadVarUint();
            Debug.Assert(_dsCurVal >= 0);
            return _dsCurVal;
        }

        public long ReadDsLength()
        {
            var diff = Reader.ReadVarUint() + 1;
            Debug.Assert(diff >= 0);
            _dsCurVal += diff;
            return diff;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing && !_leaveOpen)
                {
                    Reader?.Dispose();
                }

                Reader = null;
                Disposed = true;
            }
        }

        [Conditional("DEBUG")]
        protected void CheckDisposed()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(GetType().ToString());
            }
        }
    }

    internal sealed class UpdateDecoderV2 : DSDecoderV2, IUpdateDecoder
    {
        /// <summary>
        /// List of cached keys. If the keys[id] does not exist, we read a new key from
        /// the string encoder and push it to keys.
        /// </summary>
        private List<string> _keys;
        private IntDiffOptRleDecoder _keyClockDecoder;
        private UintOptRleDecoder _clientDecoder;
        private IntDiffOptRleDecoder _leftClockDecoder;
        private IntDiffOptRleDecoder _rightClockDecoder;
        private RleDecoder _infoDecoder;
        private StringDecoder _stringDecoder;
        private RleDecoder _parentInfoDecoder;
        private UintOptRleDecoder _typeRefDecoder;
        private UintOptRleDecoder _lengthDecoder;

        public UpdateDecoderV2(Stream input, bool leaveOpen = false)
            : base(input, leaveOpen)
        {
            _keys = new List<string>();

            // Read feature flag - currently unused.
            Reader.ReadByte();

            _keyClockDecoder = new IntDiffOptRleDecoder(Reader.ReadVarUint8ArrayAsStream());
            _clientDecoder = new UintOptRleDecoder(Reader.ReadVarUint8ArrayAsStream());
            _leftClockDecoder = new IntDiffOptRleDecoder(Reader.ReadVarUint8ArrayAsStream());
            _rightClockDecoder = new IntDiffOptRleDecoder(Reader.ReadVarUint8ArrayAsStream());
            _infoDecoder = new RleDecoder(Reader.ReadVarUint8ArrayAsStream());
            _stringDecoder = new StringDecoder(Reader.ReadVarUint8ArrayAsStream());
            _parentInfoDecoder = new RleDecoder(Reader.ReadVarUint8ArrayAsStream());
            _typeRefDecoder = new UintOptRleDecoder(Reader.ReadVarUint8ArrayAsStream());
            _lengthDecoder = new UintOptRleDecoder(Reader.ReadVarUint8ArrayAsStream());
        }

        public ID ReadLeftId()
        {
            CheckDisposed();
            return new ID(_clientDecoder.Read(), _leftClockDecoder.Read());
        }

        public ID ReadRightId()
        {
            CheckDisposed();
            return new ID(_clientDecoder.Read(), _rightClockDecoder.Read());
        }

        /// <summary>
        /// Read the next client Id.
        /// </summary>
        public long ReadClient()
        {
            CheckDisposed();
            return _clientDecoder.Read();
        }

        public byte ReadInfo()
        {
            CheckDisposed();
            return _infoDecoder.Read();
        }

        public string ReadString()
        {
            CheckDisposed();
            return _stringDecoder.Read();
        }

        public bool ReadParentInfo()
        {
            CheckDisposed();
            return _parentInfoDecoder.Read() == 1;
        }

        public uint ReadTypeRef()
        {
            CheckDisposed();
            return _typeRefDecoder.Read();
        }

        public int ReadLength()
        {
            CheckDisposed();

            var value = (int)_lengthDecoder.Read();
            Debug.Assert(value >= 0);
            return value;
        }

        public object ReadAny()
        {
            CheckDisposed();
            var obj = Reader.ReadAny();
            return obj;
        }

        public byte[] ReadBuffer()
        {
            CheckDisposed();
            return Reader.ReadVarUint8Array();
        }

        public string ReadKey()
        {
            CheckDisposed();

            var keyClock = (int)_keyClockDecoder.Read();
            if (keyClock < _keys.Count)
            {
                return _keys[keyClock];
            }
            else
            {
                var key = _stringDecoder.Read();
                _keys.Add(key);
                return key;
            }
        }

        public object ReadJson()
        {
            CheckDisposed();

            var jsonString = Reader.ReadVarString();
            var result = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonString);
            return result;
        }

        protected override void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    _keyClockDecoder?.Dispose();
                    _clientDecoder?.Dispose();
                    _leftClockDecoder?.Dispose();
                    _rightClockDecoder?.Dispose();
                    _infoDecoder?.Dispose();
                    _stringDecoder?.Dispose();
                    _parentInfoDecoder?.Dispose();
                    _typeRefDecoder?.Dispose();
                    _lengthDecoder?.Dispose();
                }

                _keyClockDecoder = null;
                _clientDecoder = null;
                _leftClockDecoder = null;
                _rightClockDecoder = null;
                _infoDecoder = null;
                _stringDecoder = null;
                _parentInfoDecoder = null;
                _typeRefDecoder = null;
                _lengthDecoder = null;
            }

            base.Dispose(disposing);
        }
    }
}
