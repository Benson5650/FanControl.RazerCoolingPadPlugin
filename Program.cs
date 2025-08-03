using System;
using System.Linq;
using FanControl.Plugins;
using HidSharp;

namespace FanControl.RazerCurvePlugin
{
    /// <summary>
    /// FanControl 外掛：只支援 Curve 模式 (Auto-Curve Update)
    /// 0 % ⇒ 500 RPM, 100 % ⇒ 3200 RPM
    /// </summary>
    public sealed class RazerCurvePlugin : IPlugin2
    {
        public string Name => "Razer Cooling Pad - Curve";

        // ─────────── 固定參數 ───────────
        private const int VID = 0x1532, PID = 0x0F43;
        private const int REPORT_LEN = 91;       // 1(ID) + 90
        private const byte REPORT_ID = 0x00;

        private const int MIN_RPM = 500;
        private const int MAX_RPM = 3200;
        private const int RANGE   = MAX_RPM - MIN_RPM; // 2700

        // payload 位移 (+1 因 ReportID 佔 buf[0])
        private const int REPORT_CODE = 8;   // offset 7
        private const int SUB_VER     = 9;   // offset 8
        private const int CURVE_ID    = 10;  // offset 9
        private const int RPM_L       = 11;  // offset10
        private const int RPM_H       = 12;  // offset11
        private const int CHK_L       = 89;  // offset88
        private const int CHK_H       = 90;  // offset89

        // 90-byte 樣板（Wireshark 抓的原封包；後 78 byte 直接補 0）
        private static readonly byte[] Header90 = new byte[90]
        {
            // 0–9
            0x00, 0x02, 0x00, 0x00, 0x00, 0x03, 0x0D, 0x10, 0x01, 0x02,
            // 10–11 先放 2700RPM，可被覆寫
            0x36, 0x00,
            // 12–89 全部補 0
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };

        // ─────────── HID ───────────
        private HidStream? _stream;
        private readonly IPluginLogger _log;

        // sensors
        private readonly RpmSensor _rpmSensor;
        private readonly CurveControl _curveControl;

        public RazerCurvePlugin(IPluginLogger logger)
        {
            _log          = logger;
            _rpmSensor    = new RpmSensor(logger);
            _curveControl = new CurveControl(SetCurveRpm, logger);
        }

        // ─────────── IPlugin2 life-cycle ───────────
        public void Initialize()
        {
            try
            {
                var dev = DeviceList.Local
                          .GetHidDevices(VID, PID)
                          .FirstOrDefault(d => d.GetMaxFeatureReportLength() == REPORT_LEN);
                if (dev == null) throw new Exception("找不到裝置");

                if (!dev.TryOpen(out _stream))
                    throw new Exception("開啟 HID 失敗");

                _rpmSensor.Attach(_stream);
            }
            catch (Exception ex)
            {
                _log.Log($"[CurvePlugin] 初始化失敗：{ex.Message}");
            }
        }

        public void Load(IPluginSensorsContainer c)
        {
            if (_stream == null) return;
            c.FanSensors    .Add(_rpmSensor);
            c.ControlSensors.Add(_curveControl);
        }

        public void Update() => _rpmSensor.Update();

        public void Close()
        {
            try { _stream?.Dispose(); } catch { }
            _stream = null;
        }

        // ─────────── 實際送 Curve 封包 ───────────
        private void SetCurveRpm(int rpm)
        {
            if (_stream == null) return;

            // 建立 91-byte buffer
            byte[] buf = new byte[REPORT_LEN];
            buf[0] = REPORT_ID;
            Buffer.BlockCopy(Header90, 0, buf, 1, 90);

            // 專用欄位
            buf[REPORT_CODE] = 0x01; // Auto-Curve
            buf[SUB_VER]     = 0x01;
            buf[CURVE_ID]    = 0x05;

            // 寫入 RPM
            rpm = Math.Clamp(rpm, MIN_RPM, MAX_RPM);
            int raw = (int)Math.Round(rpm / 50.0);
            buf[RPM_L] = (byte)(raw & 0xFF);
            buf[RPM_H] = (byte)(raw >> 8);

            // Checksum
            buf[CHK_L] = (byte)(buf[RPM_L] ^ 0x0B);
            buf[CHK_H] = 0x00;

            try { _stream.SetFeature(buf); }
            catch (Exception ex) { _log.Log($"[CurvePlugin] HID 送出失敗：{ex.Message}"); }
        }

        // ─────────── RPM Sensor ───────────
        private sealed class RpmSensor : IPluginSensor
        {
            private HidStream? _s;
            private readonly IPluginLogger _l;

            public string Id   => "RazerCooling.RPM";
            public string Name => "Cooling Pad RPM";
            public float? Value { get; private set; }

            public RpmSensor(IPluginLogger l) => _l = l;
            public void Attach(HidStream s)   => _s = s;

            public void Update()
            {
                if (_s == null) { Value = null; return; }
                try
                {
                    byte[] r = new byte[REPORT_LEN];
                    r[0] = REPORT_ID;
                    _s.GetFeature(r);

                    int raw = r[RPM_L] | (r[RPM_H] << 8);
                    Value = raw * 50f;
                }
                catch { Value = null; }
            }
        }

        // ─────────── Curve Control ───────────
        private sealed class CurveControl : IPluginControlSensor
        {
            private readonly Action<int> _send;
            private readonly IPluginLogger _log;

            public string Id   => "RazerCooling.Curve";
            public string Name => "Cooling Pad Curve %";
            public float? Value { get; private set; }

            public CurveControl(Action<int> s, IPluginLogger l)
            { _send = s; _log = l; }

            public void Set(float percent)
            {
                percent = Math.Clamp(percent, 0f, 100f);
                Value   = percent;

                int rpm = (int)Math.Round(MIN_RPM + percent / 100f * RANGE);
                rpm = (int)Math.Round(rpm / 50.0) * 50; // 50-step
                _send(rpm);
            }

            public void Reset() => Set(0);
            public void Update() { }
        }
    }
}
