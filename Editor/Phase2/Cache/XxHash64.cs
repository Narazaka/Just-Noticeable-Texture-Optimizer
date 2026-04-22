using System;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Cache
{
    /// <summary>
    /// XXHash64 の簡易インクリメンタル実装。
    /// Append() で chunk を追加し、GetCurrentHashAsUInt64() で最終値を取得。
    /// </summary>
    public class XxHash64
    {
        const ulong Prime1 = 11400714785074694791UL;
        const ulong Prime2 = 14029467366897019727UL;
        const ulong Prime3 = 1609587929392839161UL;
        const ulong Prime4 = 9650029242287828579UL;
        const ulong Prime5 = 2870177450012600261UL;

        ulong _v1, _v2, _v3, _v4;
        readonly byte[] _buffer = new byte[32];
        int _bufLen;
        ulong _totalLen;
        readonly ulong _seed;

        public XxHash64(ulong seed = 0)
        {
            _seed = seed;
            Reset();
        }

        public void Reset()
        {
            _v1 = _seed + Prime1 + Prime2;
            _v2 = _seed + Prime2;
            _v3 = _seed;
            _v4 = _seed - Prime1;
            _bufLen = 0;
            _totalLen = 0;
        }

        public void Append(byte[] data) => Append(data, 0, data.Length);

        public void Append(byte[] data, int offset, int count)
        {
            _totalLen += (ulong)count;
            if (_bufLen + count < 32)
            {
                Buffer.BlockCopy(data, offset, _buffer, _bufLen, count);
                _bufLen += count;
                return;
            }

            int pos = 0;
            if (_bufLen > 0)
            {
                int fill = 32 - _bufLen;
                Buffer.BlockCopy(data, offset, _buffer, _bufLen, fill);
                ProcessStripe(_buffer, 0);
                pos = fill;
                _bufLen = 0;
            }
            while (pos + 32 <= count)
            {
                ProcessStripe(data, offset + pos);
                pos += 32;
            }
            if (pos < count)
            {
                _bufLen = count - pos;
                Buffer.BlockCopy(data, offset + pos, _buffer, 0, _bufLen);
            }
        }

        void ProcessStripe(byte[] src, int idx)
        {
            _v1 = Round(_v1, BitConverter.ToUInt64(src, idx + 0));
            _v2 = Round(_v2, BitConverter.ToUInt64(src, idx + 8));
            _v3 = Round(_v3, BitConverter.ToUInt64(src, idx + 16));
            _v4 = Round(_v4, BitConverter.ToUInt64(src, idx + 24));
        }

        static ulong Round(ulong acc, ulong input)
        {
            acc += input * Prime2;
            acc = (acc << 31) | (acc >> 33);
            acc *= Prime1;
            return acc;
        }

        public ulong GetCurrentHashAsUInt64()
        {
            ulong h;
            if (_totalLen >= 32)
            {
                h = ((_v1 << 1) | (_v1 >> 63))
                  + ((_v2 << 7) | (_v2 >> 57))
                  + ((_v3 << 12) | (_v3 >> 52))
                  + ((_v4 << 18) | (_v4 >> 46));
                h = MergeRound(h, _v1);
                h = MergeRound(h, _v2);
                h = MergeRound(h, _v3);
                h = MergeRound(h, _v4);
            }
            else
            {
                h = _seed + Prime5;
            }
            h += _totalLen;

            int remaining = _bufLen, pos = 0;
            while (pos + 8 <= remaining)
            {
                h ^= Round(0, BitConverter.ToUInt64(_buffer, pos));
                h = ((h << 27) | (h >> 37)) * Prime1 + Prime4;
                pos += 8;
            }
            if (pos + 4 <= remaining)
            {
                h ^= (ulong)BitConverter.ToUInt32(_buffer, pos) * Prime1;
                h = ((h << 23) | (h >> 41)) * Prime2 + Prime3;
                pos += 4;
            }
            while (pos < remaining)
            {
                h ^= (ulong)_buffer[pos] * Prime5;
                h = ((h << 11) | (h >> 53)) * Prime1;
                pos++;
            }
            h ^= h >> 33;
            h *= Prime2;
            h ^= h >> 29;
            h *= Prime3;
            h ^= h >> 32;
            return h;
        }

        static ulong MergeRound(ulong acc, ulong v)
        {
            v = Round(0, v);
            acc ^= v;
            acc = acc * Prime1 + Prime4;
            return acc;
        }
    }
}
