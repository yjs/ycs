// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Ycs
{
    public class DSEncoderV2 : IDSEncoder
    {
        private int _dsCurVal;
        private MemoryStream _restStream;

        public DSEncoderV2()
        {
            _dsCurVal = 0;

            _restStream = new MemoryStream();
            RestWriter = new BinaryWriter(_restStream, Encoding.UTF8, leaveOpen: true);
        }

        public BinaryWriter RestWriter { get; private set; }
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

        public void WriteDsClock(int clock)
        {
            int diff = clock - _dsCurVal;
            Debug.Assert(diff >= 0);
            _dsCurVal = clock;
            RestWriter.WriteVarUint((uint)diff);
        }

        public void WriteDsLength(int length)
        {
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            RestWriter.WriteVarUint((uint)(length - 1));
            _dsCurVal += length;
        }

        public virtual byte[] ToArray()
        {
            RestWriter.Flush();
            return _restStream.ToArray();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    RestWriter.Dispose();
                    _restStream.Dispose();
                }

                RestWriter = null;
                _restStream = null;

                Disposed = true;
            }
        }
    }

    public sealed class UpdateEncoderV2 : DSEncoderV2, IUpdateEncoder
    {
        // Refers to the next unique key-identifier to be used.
        private int _keyClock;
        private IDictionary<string, int> _keyMap;

        private IntDiffOptRleEncoder _keyClockEncoder;
        private UintOptRleEncoder _clientEncoder;
        private IntDiffOptRleEncoder _leftClockEncoder;
        private IntDiffOptRleEncoder _rightClockEncoder;
        private RleEncoder _infoEncoder;
        private StringEncoder _stringEncoder;
        private RleEncoder _parentInfoEncoder;
        private UintOptRleEncoder _typeRefEncoder;
        private UintOptRleEncoder _lengthEncoder;

        public UpdateEncoderV2()
        {
            _keyClock = 0;

            _keyMap = new Dictionary<string, int>();
            _keyClockEncoder = new IntDiffOptRleEncoder();
            _clientEncoder = new UintOptRleEncoder();
            _leftClockEncoder = new IntDiffOptRleEncoder();
            _rightClockEncoder = new IntDiffOptRleEncoder();
            _infoEncoder = new RleEncoder();
            _stringEncoder = new StringEncoder();
            _parentInfoEncoder = new RleEncoder();
            _typeRefEncoder = new UintOptRleEncoder();
            _lengthEncoder = new UintOptRleEncoder();
        }

        public override byte[] ToArray()
        {
            using var stream = new MemoryStream();

            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                // Read the feature flag that might be used in the future.
                writer.Write((byte)0);

                // TODO: [alekseyk] Maybe pass the writer directly instead of using ToArray()?
                writer.WriteVarUint8Array(_keyClockEncoder.ToArray());
                writer.WriteVarUint8Array(_clientEncoder.ToArray());
                writer.WriteVarUint8Array(_leftClockEncoder.ToArray());
                writer.WriteVarUint8Array(_rightClockEncoder.ToArray());
                writer.WriteVarUint8Array(_infoEncoder.ToArray());
                writer.WriteVarUint8Array(_stringEncoder.ToArray());
                writer.WriteVarUint8Array(_parentInfoEncoder.ToArray());
                writer.WriteVarUint8Array(_typeRefEncoder.ToArray());
                writer.WriteVarUint8Array(_lengthEncoder.ToArray());

                // Append the rest of the data from the RestWriter.
                // Note it's not the 'WriteVarUint8Array'.
                writer.Write(base.ToArray());
            }

            return stream.ToArray();
        }

        public void WriteLeftId(ID id)
        {
            _clientEncoder.Write((uint)id.Client);
            _leftClockEncoder.Write(id.Clock);
        }

        public void WriteRightId(ID id)
        {
            _clientEncoder.Write((uint)id.Client);
            _rightClockEncoder.Write(id.Clock);
        }

        public void WriteClient(int client)
        {
            _clientEncoder.Write((uint)client);
        }

        public void WriteInfo(byte info)
        {
            _infoEncoder.Write(info);
        }

        public void WriteString(string s)
        {
            _stringEncoder.Write(s);
        }

        public void WriteParentInfo(bool isYKey)
        {
            _parentInfoEncoder.Write((byte)(isYKey ? 1 : 0));
        }

        public void WriteTypeRef(uint info)
        {
            _typeRefEncoder.Write(info);
        }

        public void WriteLength(int len)
        {
            Debug.Assert(len >= 0);
            _lengthEncoder.Write((uint)len);
        }

        public void WriteAny(object any)
        {
            RestWriter.WriteAny(any);
        }

        public void WriteBuffer(byte[] data)
        {
            RestWriter.WriteVarUint8Array(data);
        }

        /// <summary>
        /// Property keys are often reused. For example, in y-prosemirror the key 'bold'
        /// might occur very often. For a 3D application, the key 'position' might occur often.
        /// <br/>
        /// We can these keys in a map and refer to them via a unique number.
        /// </summary>
        public void WriteKey(string key)
        {
            _keyClockEncoder.Write(_keyClock++);

            if (!_keyMap.ContainsKey(key))
            {
                _stringEncoder.Write(key);
            }
        }

        public void WriteJson<T>(T any)
        {
            var jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(any, typeof(T), null);
            RestWriter.WriteVarString(jsonString);
        }

        protected override void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    _keyMap.Clear();
                    _keyClockEncoder.Dispose();
                    _clientEncoder.Dispose();
                    _leftClockEncoder.Dispose();
                    _rightClockEncoder.Dispose();
                    _infoEncoder.Dispose();
                    _stringEncoder.Dispose();
                    _parentInfoEncoder.Dispose();
                    _typeRefEncoder.Dispose();
                    _lengthEncoder.Dispose();
                }

                _keyMap = null;
                _keyClockEncoder = null;
                _clientEncoder = null;
                _leftClockEncoder = null;
                _rightClockEncoder = null;
                _infoEncoder = null;
                _stringEncoder = null;
                _parentInfoEncoder = null;
                _typeRefEncoder = null;
                _lengthEncoder = null;
            }

            base.Dispose(disposing);
        }
    }
}
