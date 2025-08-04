using System;
using System.Linq;
using FanControl.Plugins;
using HidSharp;

namespace FanControl.RazerCurvePlugin
{
    public sealed class RazerCurvePlugin : IPlugin2
    {
        public string Name => "Razer Cooling Pad - Curve (Hot-Plug)";

        // ────────── 常量 ──────────
        private const int VID = 0x1532, PID = 0x0F43;
        private const int REPORT_LEN = 91;
        private const byte REPORT_ID = 0x00;
        private const int MIN_RPM = 500, MAX_RPM = 3200, RANGE = MAX_RPM - MIN_RPM;

        // payload (+1)
        private const int REPORT_CODE = 8, SUB_VER = 9, CURVE_ID = 10,
                          RPM_L = 11, RPM_H = 12, CHK_L = 89, CHK_H = 90;

        private static readonly byte[] Header90 = new byte[90]
        {
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

        // ────────── HID 與同步 ──────────
        private HidStream? _stream;
        private HidDevice? _device;                // ★ 保留開啟的裝置
        private readonly object _sync = new();
        private readonly IPluginLogger _log;

        // sensors
        private readonly RpmSensor    _rpmSensor;
        private readonly CurveControl _curveControl;

        public RazerCurvePlugin(IPluginLogger logger)
        {
            _log          = logger;
            _rpmSensor    = new RpmSensor(logger, () => _stream);
            _curveControl = new CurveControl(SetCurveRpm, SendOff, logger, () => _stream);
        }

        // ────────── Life-cycle ──────────
        public void Initialize()
        {
            TryOpenDevice();
            DeviceList.Local.Changed += OnUsbChanged;
        }

        public void Load(IPluginSensorsContainer c)
        {
            c.FanSensors.Add(_rpmSensor);
            c.ControlSensors.Add(_curveControl);
        }

        public void Update() => _rpmSensor.Update();

        public void Close()
        {
            SendOff();
            lock (_sync) { _stream?.Dispose(); _stream = null; _device = null; }
            DeviceList.Local.Changed -= OnUsbChanged;
        }

        // ────────── USB 變動監聽 ──────────
        private void OnUsbChanged(object? sender, DeviceListChangedEventArgs e)
        {
            lock (_sync)
            {
                // 1) 目前無連線 → 嘗試接管
                if (_stream == null) { TryOpenDevice(); return; }

                // 2) 檢查已連線裝置是否仍存在
                bool stillPresent = DeviceList.Local.GetHidDevices(VID, PID)
                                    .Any(d => d.DevicePath == _device?.DevicePath);   // ★
                if (!stillPresent)
                {
                    _log.Log("[RazerCurve] 散熱器已拔除，RPM 置 0");
                    _stream.Dispose();
                    _stream  = null;
                    _device  = null;
                }
            }
        }

        private void TryOpenDevice()
        {
            lock (_sync)
            {
                if (_stream != null) return;
                var dev = DeviceList.Local.GetHidDevices(VID, PID)
                          .FirstOrDefault(d => d.GetMaxFeatureReportLength() == REPORT_LEN);
                if (dev != null && dev.TryOpen(out var s))
                {
                    _device = dev;                // ★ 保存 HidDevice
                    _stream = s;
                    _log.Log("[RazerCurve] 散熱器已連線並接管控制");
                }
            }
        }

        // ────────── 送封包 ──────────
        private void SetCurveRpm(int rpm)
        {
            if (_stream == null) return;

            byte[] buf = new byte[REPORT_LEN];
            buf[0] = REPORT_ID;
            Buffer.BlockCopy(Header90, 0, buf, 1, 90);

            buf[REPORT_CODE] = 0x01; buf[SUB_VER] = 0x01; buf[CURVE_ID] = 0x05;

            rpm = Math.Clamp(rpm, MIN_RPM, MAX_RPM);
            int raw = (int)Math.Round(rpm / 50.0);
            buf[RPM_L] = (byte)(raw & 0xFF);
            buf[RPM_H] = (byte)(raw >> 8);

            buf[CHK_L] = (byte)(buf[RPM_L] ^ 0x0B); buf[CHK_H] = 0x00;

            try { _stream.SetFeature(buf); }
            catch (Exception ex) { _log.Log($"[RazerCurve] 送 Curve 失敗: {ex.Message}"); }
        }

        private void SendOff()
        {
            if (_stream == null) return;
            byte[] buf = new byte[REPORT_LEN];
            buf[0] = REPORT_ID;
            Buffer.BlockCopy(Header90, 0, buf, 1, 90);

            buf[REPORT_CODE] = 0x10; buf[SUB_VER] = 0x00; buf[CURVE_ID] = 0x06;
            buf[RPM_L] = buf[RPM_H] = 0x00; buf[CHK_L] = 0x18; buf[CHK_H] = 0x00;

            try { _stream.SetFeature(buf); } catch { }
        }

        // ────────── Sensor ──────────
        private sealed class RpmSensor : IPluginSensor
        {
            private readonly IPluginLogger _l; private readonly Func<HidStream?> _get;
            public RpmSensor(IPluginLogger l, Func<HidStream?> getter) { _l = l; _get = getter; }
            public string Id => "RazerCooling.RPM"; public string Name => "Cooling Pad RPM";
            public float? Value { get; private set; }

            public void Update()
            {
                var s = _get(); if (s == null) { Value = 0f; return; }
                try
                {
                    byte[] r = new byte[REPORT_LEN]; r[0] = REPORT_ID; s.GetFeature(r);
                    int raw = r[RPM_L] | (r[RPM_H] << 8); Value = raw * 50f;
                }
                catch { Value = 0f; }
            }
        }

        // ────────── Control ──────────
        private sealed class CurveControl : IPluginControlSensor
        {
            private readonly Action<int> _send; private readonly Action _off;
            private readonly Func<HidStream?> _get;
            public CurveControl(Action<int> s, Action off, IPluginLogger _, Func<HidStream?> g)
            { _send = s; _off = off; _get = g; }

            public string Id => "RazerCooling.Curve"; public string Name => "Cooling Pad Curve %";
            public float? Value { get; private set; }

            public void Set(float percent)
            {
                percent = Math.Clamp(percent, 0f, 100f); Value = percent;
                int rpm = (int)Math.Round(MIN_RPM + percent / 100f * RANGE);
                rpm = (int)Math.Round(rpm / 50.0) * 50;
                if (_get() != null) _send(rpm);
            }
            public void Reset() => _off();
            public void Update() { }
        }
    }
}
