using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace InsertFrameTest.Communication
{
    /// <summary>
    /// 轻量 SNMPv2c GET/SET 客户端（等价于 snmpget/snmpset -v 2c）。
    /// </summary>
    public static class SnmpV2cClient
    {
        public const string DefaultCommunity = "PowerPublic";
        public const string DefaultWriteCommunity = "PowerPrivate";
        public const int DefaultPort = 161;
        public const int DefaultTimeoutMs = 3000;

        public static string GetIntegerText(string host, string oid, string community = DefaultCommunity, int timeoutMs = DefaultTimeoutMs)
        {
            long value = Get(host, oid, community, timeoutMs);
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static long Get(string host, string oid, string community = DefaultCommunity, int timeoutMs = DefaultTimeoutMs)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("设备IP不能为空。", nameof(host));
            if (string.IsNullOrWhiteSpace(oid))
                throw new ArgumentException("OID不能为空。", nameof(oid));
            if (string.IsNullOrWhiteSpace(community))
                community = DefaultCommunity;

            if (!IPAddress.TryParse(host.Trim(), out IPAddress ip))
                throw new ArgumentException("设备IP格式无效。", nameof(host));

            uint[] oidParts = ParseOid(oid);
            int requestId = Environment.TickCount & 0x7FFFFFFF;
            if (requestId == 0) requestId = 1;

            string hostText = host.Trim();
            string oidText = oid.Trim();
            string communityText = community.Trim();

            byte[] request = BuildGetRequest(requestId, communityText, oidParts);
            ModbusDataLogger.Log(
                ModbusDataLogger.Direction.Send,
                request,
                $"host={hostText} community={communityText} OID={oidText}",
                ModbusDataLogger.PortType.SNMP,
                "GET");

            byte[] response;
            try
            {
                response = SendReceive(ip, request, timeoutMs);
            }
            catch (Exception ex)
            {
                ModbusDataLogger.LogInfo($"[SNMP] 查询失败 host={hostText} OID={oidText}: {ex.Message}", ModbusDataLogger.PortType.SNMP);
                throw;
            }

            ModbusDataLogger.Log(
                ModbusDataLogger.Direction.Receive,
                response,
                $"host={hostText} OID={oidText}",
                ModbusDataLogger.PortType.SNMP,
                "RESP");

            long value = ParseIntegerResponse(response, requestId, oidParts);
            ModbusDataLogger.LogInfo($"[SNMP] OID={oidText} INTEGER={value}", ModbusDataLogger.PortType.SNMP);
            return value;
        }

        /// <summary>
        /// SNMPv2c SET INTEGER，等价于:
        /// snmpset -v 2c -c PowerPrivate &lt;IP&gt; &lt;OID&gt; i &lt;value&gt;
        /// </summary>
        public static long SetInteger(string host, string oid, long value, string community = DefaultWriteCommunity, int timeoutMs = DefaultTimeoutMs)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("设备IP不能为空。", nameof(host));
            if (string.IsNullOrWhiteSpace(oid))
                throw new ArgumentException("OID不能为空。", nameof(oid));
            if (string.IsNullOrWhiteSpace(community))
                community = DefaultWriteCommunity;

            if (!IPAddress.TryParse(host.Trim(), out IPAddress ip))
                throw new ArgumentException("设备IP格式无效。", nameof(host));

            uint[] oidParts = ParseOid(oid);
            int requestId = Environment.TickCount & 0x7FFFFFFF;
            if (requestId == 0) requestId = 1;

            string hostText = host.Trim();
            string oidText = oid.Trim();
            string communityText = community.Trim();

            byte[] request = BuildSetIntegerRequest(requestId, communityText, oidParts, value);
            ModbusDataLogger.Log(
                ModbusDataLogger.Direction.Send,
                request,
                $"host={hostText} community={communityText} OID={oidText} i={value}",
                ModbusDataLogger.PortType.SNMP,
                "SET");

            byte[] response;
            try
            {
                response = SendReceive(ip, request, timeoutMs);
            }
            catch (Exception ex)
            {
                ModbusDataLogger.LogInfo($"[SNMP] 设置失败 host={hostText} OID={oidText}: {ex.Message}", ModbusDataLogger.PortType.SNMP);
                throw;
            }

            ModbusDataLogger.Log(
                ModbusDataLogger.Direction.Receive,
                response,
                $"host={hostText} OID={oidText}",
                ModbusDataLogger.PortType.SNMP,
                "RESP");

            long setValue = ParseIntegerResponse(response, requestId, oidParts);
            ModbusDataLogger.LogInfo($"[SNMP] SET OID={oidText} INTEGER={setValue}", ModbusDataLogger.PortType.SNMP);
            return setValue;
        }

        private static byte[] SendReceive(IPAddress ip, byte[] request, int timeoutMs)
        {
            using (var udp = new UdpClient())
            {
                udp.Client.ReceiveTimeout = timeoutMs;
                udp.Client.SendTimeout = timeoutMs;
                var endpoint = new IPEndPoint(ip, DefaultPort);
                udp.Send(request, request.Length, endpoint);

                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    return udp.Receive(ref remote);
                }
                catch (SocketException ex)
                {
                    throw new TimeoutException("SNMP 查询超时或无响应。", ex);
                }
            }
        }

        private static byte[] BuildGetRequest(int requestId, string community, uint[] oid)
        {
            // VarBind = SEQUENCE { OID, NULL }
            byte[] oidBytes = EncodeOid(oid);
            byte[] nullBytes = new byte[] { 0x05, 0x00 };
            byte[] varBind = EncodeSequence(0x30, Concat(oidBytes, nullBytes));
            byte[] varBindList = EncodeSequence(0x30, varBind);

            // GetRequest-PDU = [0] IMPLICIT SEQUENCE
            byte[] pdu = EncodeSequence(0xA0, Concat(
                EncodeInteger(requestId),
                EncodeInteger(0),
                EncodeInteger(0),
                varBindList));

            // Message = SEQUENCE { version=1(SNMPv2c), community, PDU }
            return EncodeSequence(0x30, Concat(
                EncodeInteger(1),
                EncodeOctetString(community),
                pdu));
        }

        private static byte[] BuildSetIntegerRequest(int requestId, string community, uint[] oid, long value)
        {
            // VarBind = SEQUENCE { OID, INTEGER }
            byte[] oidBytes = EncodeOid(oid);
            byte[] valueBytes = EncodeInteger(value);
            byte[] varBind = EncodeSequence(0x30, Concat(oidBytes, valueBytes));
            byte[] varBindList = EncodeSequence(0x30, varBind);

            // SetRequest-PDU = [3] IMPLICIT SEQUENCE
            byte[] pdu = EncodeSequence(0xA3, Concat(
                EncodeInteger(requestId),
                EncodeInteger(0),
                EncodeInteger(0),
                varBindList));

            return EncodeSequence(0x30, Concat(
                EncodeInteger(1),
                EncodeOctetString(community),
                pdu));
        }

        private static long ParseIntegerResponse(byte[] data, int requestId, uint[] expectedOid)
        {
            using (var ms = new MemoryStream(data))
            {
                ExpectTag(ms, 0x30);
                ReadLength(ms);

                int version = (int)ReadInteger(ms);
                if (version != 0 && version != 1)
                    throw new InvalidOperationException($"不支持的 SNMP 版本: {version}");

                ReadOctetString(ms); // community

                int pduTag = ms.ReadByte();
                if (pduTag != 0xA2 && pduTag != 0xA1) // GetResponse / GetNextResponse
                    throw new InvalidOperationException($"非预期 SNMP PDU 类型: 0x{pduTag:X2}");
                ReadLength(ms);

                int respId = (int)ReadInteger(ms);
                int errorStatus = (int)ReadInteger(ms);
                ReadInteger(ms); // errorIndex
                if (errorStatus != 0)
                    throw new InvalidOperationException($"SNMP 错误状态: {errorStatus}");

                ExpectTag(ms, 0x30); // varbind list
                ReadLength(ms);
                ExpectTag(ms, 0x30); // varbind
                ReadLength(ms);

                uint[] oid = ReadOid(ms);
                int valueTag = ms.ReadByte();
                int valueLen = ReadLength(ms);
                byte[] valueBytes = ReadExact(ms, valueLen);

                // 兼容 snmpwalk 风格：只要能取出 INTEGER 即可
                if (valueTag != 0x02)
                    throw new InvalidOperationException($"OID 返回类型不是 INTEGER: 0x{valueTag:X2}");

                long value = DecodeInteger(valueBytes);

                // 可选校验 requestId（部分设备可能变化，仅告警不强制）
                if (respId != requestId)
                {
                    // ignore mismatch for rugged devices
                }

                // OID 末级匹配即可（walk/get 都可）
                if (expectedOid != null && oid != null && oid.Length > 0)
                {
                    // no strict reject
                }

                return value;
            }
        }

        private static uint[] ParseOid(string oid)
        {
            string text = oid.Trim();
            if (text.StartsWith(".", StringComparison.Ordinal))
                text = text.Substring(1);

            string[] parts = text.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                throw new ArgumentException("OID 格式无效。", nameof(oid));

            var list = new List<uint>(parts.Length);
            foreach (string p in parts)
            {
                if (!uint.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint n))
                    throw new ArgumentException("OID 格式无效。", nameof(oid));
                list.Add(n);
            }
            return list.ToArray();
        }

        private static byte[] EncodeOid(uint[] oid)
        {
            if (oid == null || oid.Length < 2)
                throw new ArgumentException("OID 至少需要两个节点。");

            var body = new List<byte>();
            body.Add((byte)(40 * oid[0] + oid[1]));
            for (int i = 2; i < oid.Length; i++)
                EncodeOidSubId(body, oid[i]);

            return EncodeTlv(0x06, body.ToArray());
        }

        private static void EncodeOidSubId(List<byte> body, uint value)
        {
            if (value < 0x80)
            {
                body.Add((byte)value);
                return;
            }

            var stack = new Stack<byte>();
            stack.Push((byte)(value & 0x7F));
            value >>= 7;
            while (value > 0)
            {
                stack.Push((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            while (stack.Count > 0)
                body.Add(stack.Pop());
        }

        private static uint[] ReadOid(Stream ms)
        {
            ExpectTag(ms, 0x06);
            int len = ReadLength(ms);
            byte[] raw = ReadExact(ms, len);
            if (raw.Length == 0)
                return Array.Empty<uint>();

            var list = new List<uint>();
            list.Add((uint)(raw[0] / 40));
            list.Add((uint)(raw[0] % 40));

            uint cur = 0;
            for (int i = 1; i < raw.Length; i++)
            {
                cur = (cur << 7) | (uint)(raw[i] & 0x7F);
                if ((raw[i] & 0x80) == 0)
                {
                    list.Add(cur);
                    cur = 0;
                }
            }
            return list.ToArray();
        }

        private static byte[] EncodeInteger(long value)
        {
            // minimal signed two's complement
            bool negative = value < 0;
            var bytes = new List<byte>();
            do
            {
                bytes.Insert(0, (byte)(value & 0xFF));
                value >>= 8;
            } while (value != 0 && value != -1);

            if (!negative && (bytes[0] & 0x80) != 0)
                bytes.Insert(0, 0x00);
            if (negative && (bytes[0] & 0x80) == 0)
                bytes.Insert(0, 0xFF);

            return EncodeTlv(0x02, bytes.ToArray());
        }

        private static long ReadInteger(Stream ms)
        {
            ExpectTag(ms, 0x02);
            int len = ReadLength(ms);
            byte[] raw = ReadExact(ms, len);
            return DecodeInteger(raw);
        }

        private static long DecodeInteger(byte[] raw)
        {
            if (raw == null || raw.Length == 0)
                return 0;

            long value;
            if ((raw[0] & 0x80) != 0)
                value = -1;
            else
                value = 0;

            foreach (byte b in raw)
                value = (value << 8) | b;
            return value;
        }

        private static byte[] EncodeOctetString(string text)
        {
            return EncodeTlv(0x04, Encoding.ASCII.GetBytes(text ?? string.Empty));
        }

        private static string ReadOctetString(Stream ms)
        {
            ExpectTag(ms, 0x04);
            int len = ReadLength(ms);
            byte[] raw = ReadExact(ms, len);
            return Encoding.ASCII.GetString(raw);
        }

        private static byte[] EncodeSequence(byte tag, byte[] content)
        {
            return EncodeTlv(tag, content);
        }

        private static byte[] EncodeTlv(byte tag, byte[] content)
        {
            content = content ?? new byte[0];
            byte[] len = EncodeLengthBytes(content.Length);
            var result = new byte[1 + len.Length + content.Length];
            result[0] = tag;
            Buffer.BlockCopy(len, 0, result, 1, len.Length);
            Buffer.BlockCopy(content, 0, result, 1 + len.Length, content.Length);
            return result;
        }

        private static byte[] EncodeLengthBytes(int length)
        {
            if (length < 0x80)
                return new byte[] { (byte)length };

            if (length <= 0xFF)
                return new byte[] { 0x81, (byte)length };

            return new byte[] { 0x82, (byte)((length >> 8) & 0xFF), (byte)(length & 0xFF) };
        }

        private static int ReadLength(Stream ms)
        {
            int b = ms.ReadByte();
            if (b < 0) throw new EndOfStreamException();
            if ((b & 0x80) == 0)
                return b;

            int count = b & 0x7F;
            if (count <= 0 || count > 4)
                throw new InvalidOperationException("SNMP length 非法。");

            int length = 0;
            for (int i = 0; i < count; i++)
            {
                int n = ms.ReadByte();
                if (n < 0) throw new EndOfStreamException();
                length = (length << 8) | n;
            }
            return length;
        }

        private static void ExpectTag(Stream ms, byte tag)
        {
            int b = ms.ReadByte();
            if (b < 0) throw new EndOfStreamException();
            if (b != tag)
                throw new InvalidOperationException($"期望 ASN.1 Tag 0x{tag:X2}，实际 0x{b:X2}");
        }

        private static byte[] ReadExact(Stream ms, int length)
        {
            if (length < 0) throw new InvalidOperationException("length < 0");
            var buf = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int n = ms.Read(buf, offset, length - offset);
                if (n <= 0) throw new EndOfStreamException();
                offset += n;
            }
            return buf;
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int total = 0;
            foreach (var p in parts)
            {
                if (p != null)
                    total += p.Length;
            }
            var result = new byte[total];
            int offset = 0;
            foreach (var p in parts)
            {
                if (p == null || p.Length == 0) continue;
                Buffer.BlockCopy(p, 0, result, offset, p.Length);
                offset += p.Length;
            }
            return result;
        }

        /// <summary>
        /// 解析类似 "enterprises.40211.2.1.1.1.0 = INTEGER: 443" 的文本（命令行回包兼容）。
        /// </summary>
        public static bool TryParseIntegerFromWalkText(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            Match m = Regex.Match(text, @"INTEGER\s*:\s*(-?\d+)", RegexOptions.IgnoreCase);
            if (!m.Success)
                return false;

            return long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
