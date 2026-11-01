using System;
using System.IO;
using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// Calibration log (REQ-027, C-13): in development builds, one CSV file per session in
    /// Application.persistentDataPath, one row per finalized ball, flushed every row. Not saved scores; nothing reads
    /// it back in the game. Pull with: adb pull /sdcard/Android/data/com.cricketvr.prototype/files/
    /// </summary>
    public class BallLogWriter : MonoBehaviour
    {
        [SerializeField] GameConfig config;

        StreamWriter _writer;
        public string CurrentPath { get; private set; }

        bool Enabled => config != null && config.session.logBallsCsv && Debug.isDebugBuild;

        public void BeginSession(string sessionId)
        {
            EndSession();
            if (!Enabled)
                return;
            try
            {
                CurrentPath = Path.Combine(Application.persistentDataPath, $"cricketvr_{sessionId}.csv");
                _writer = new StreamWriter(CurrentPath, false);
                _writer.WriteLine(BallRecord.CsvHeader);
                _writer.Flush();
                Debug.Log($"[CricketVR] Ball log: {CurrentPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CricketVR] Ball log disabled: {e.Message}");
                _writer = null;
            }
        }

        public void Append(BallRecord record)
        {
            if (_writer == null)
                return;
            try
            {
                _writer.WriteLine(record.ToCsvRow());
                _writer.Flush();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CricketVR] Ball log write failed: {e.Message}");
            }
        }

        public void EndSession()
        {
            _writer?.Dispose();
            _writer = null;
        }

        void OnDestroy() => EndSession();

        public void Configure(GameConfig cfg) => config = cfg;
    }
}
