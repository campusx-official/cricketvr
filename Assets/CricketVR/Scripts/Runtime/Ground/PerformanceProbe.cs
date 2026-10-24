using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// AC-14 evidence in development builds (plan.md §15, T046): every <see cref="GameConfig.perfWindowSeconds"/> it
    /// logs frame rate, the share of frames on time for 72 Hz, CPU and GPU frame time (FrameTimingManager), draw
    /// calls, SetPass calls, triangles and texture memory (render counters exist only in development builds).
    /// With <see cref="GameConfig.foveationAbSeconds"/> > 0 it alternates fixed foveated rendering between the
    /// configured level and off, so one play session measures both. Read with: adb logcat -s Unity | grep Perf
    /// </summary>
    public class PerformanceProbe : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] FoveationSetup foveation;

        ProfilerRecorder _drawCalls, _setPass, _triangles, _textureMemory;
        readonly FrameTiming[] _timing = new FrameTiming[1];
        float _windowStart, _abStart;
        bool _foveationOn = true;
        int _frames, _onTime, _timed;
        double _cpu, _gpu, _cpuMax, _gpuMax;
        long _drawSum, _drawMax, _setPassMax, _triSum, _triMax;

        void OnEnable()
        {
            if (!Debug.isDebugBuild || config == null || config.perfWindowSeconds <= 0f)
            {
                enabled = false;
                return;
            }
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _textureMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Texture Memory");
            _windowStart = _abStart = Time.unscaledTime;
        }

        void OnDisable()
        {
            _drawCalls.Dispose();
            _setPass.Dispose();
            _triangles.Dispose();
            _textureMemory.Dispose();
        }

        void Update()
        {
            _frames++;
            if (Time.unscaledDeltaTime <= 1.1f / 72f) _onTime++;

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timing) > 0 && _timing[0].cpuFrameTime > 0)
            {
                _timed++;
                _cpu += _timing[0].cpuFrameTime;
                _gpu += _timing[0].gpuFrameTime;
                _cpuMax = System.Math.Max(_cpuMax, _timing[0].cpuFrameTime);
                _gpuMax = System.Math.Max(_gpuMax, _timing[0].gpuFrameTime);
            }
            long draws = _drawCalls.LastValue, tris = _triangles.LastValue;
            _drawSum += draws;
            _drawMax = System.Math.Max(_drawMax, draws);
            _setPassMax = System.Math.Max(_setPassMax, _setPass.LastValue);
            _triSum += tris;
            _triMax = System.Math.Max(_triMax, tris);

            float now = Time.unscaledTime;
            if (now - _windowStart >= config.perfWindowSeconds)
            {
                Report(now - _windowStart);
                _windowStart = now;
            }
            if (config.foveationAbSeconds > 0f && foveation != null && now - _abStart >= config.foveationAbSeconds)
            {
                _abStart = now;
                _foveationOn = !_foveationOn;
                foveation.Apply(_foveationOn ? config.foveationLevel : 0f);
                _windowStart = now; // windows never mix the two settings
                ResetWindow();
            }
        }

        void Report(float seconds)
        {
            var sb = new StringBuilder(256);
            sb.Append("[CricketVR] Perf ").Append(config.environment)
              .Append(" | foveation ").Append(foveation != null ? foveation.Level.ToString("F2") : "n/a")
              .Append(" | ").Append((_frames / seconds).ToString("F1")).Append(" fps, ")
              .Append((100f * _onTime / Mathf.Max(1, _frames)).ToString("F1")).Append("% frames on time (72 Hz)");
            if (_timed > 0)
                sb.Append(" | CPU ").Append((_cpu / _timed).ToString("F2")).Append(" ms avg / ").Append(_cpuMax.ToString("F2")).Append(" max")
                  .Append(", GPU ").Append((_gpu / _timed).ToString("F2")).Append(" ms avg / ").Append(_gpuMax.ToString("F2")).Append(" max");
            else
                sb.Append(" | CPU/GPU timing unavailable");
            sb.Append(" | draw calls ").Append(_drawSum / Mathf.Max(1, _frames)).Append(" avg / ").Append(_drawMax).Append(" max")
              .Append(", SetPass max ").Append(_setPassMax)
              .Append(", triangles ").Append(_triSum / Mathf.Max(1, _frames)).Append(" avg / ").Append(_triMax).Append(" max")
              .Append(", texture memory ").Append((_textureMemory.LastValue / (1024f * 1024f)).ToString("F1")).Append(" MB");
            Debug.Log(sb.ToString());
            ResetWindow();
        }

        void ResetWindow()
        {
            _frames = _onTime = _timed = 0;
            _cpu = _gpu = _cpuMax = _gpuMax = 0;
            _drawSum = _drawMax = _setPassMax = _triSum = _triMax = 0;
        }

        public void Configure(GameConfig cfg, FoveationSetup foveationSetup)
        {
            config = cfg;
            foveation = foveationSetup;
        }
    }
}
