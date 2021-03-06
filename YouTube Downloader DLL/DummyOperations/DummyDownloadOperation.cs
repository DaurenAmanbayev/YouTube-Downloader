﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using YouTube_Downloader_DLL.Operations;

namespace YouTube_Downloader_DLL.DummyOperations
{
    public class DummyDownloadOperation : Operation
    {
        bool _cancel;
        long _workTimeMS;
        static Random _random = new Random(Environment.TickCount);

        public DummyDownloadOperation(long workTimeMS)
        {
            _workTimeMS = workTimeMS;

            this.Duration = (long)TimeSpan.FromMilliseconds(workTimeMS).TotalSeconds;
            this.FileSize = 100 * 100000;
            this.Link = "https://www.google.no";
            this.ReportsProgress = true;
            this.Thumbnail = "https://i.ytimg.com/vi/koBVKAZ34kQ/hqdefault.jpg?custom=true&w=196&h=110&stc=true&jpg444=true&jpgq=90&sp=68&sigh=sZeir3d4uRtpKCKDx5619dwQeA8";
            this.Title = "Dummy download operation #" + _random.Next(10000);

            this.Input = this.Link;
            this.Output = @"C:\output.mp4";
        }

        #region Operation members

        public override void Pause()
        {
            this.Status = OperationStatus.Paused;
        }

        public override void Queue()
        {
            this.Status = OperationStatus.Queued;
        }

        protected override void ResumeInternal()
        {
            this.Status = OperationStatus.Working;
        }

        public override bool Stop()
        {
            _cancel = true;
            return true;
        }

        public override bool CanPause()
        {
            return this.Status == OperationStatus.Working;
        }

        public override bool CanResume()
        {
            return this.IsPaused || this.IsQueued;
        }

        public override bool CanStop()
        {
            return this.IsPaused || this.IsWorking || this.IsQueued;
        }

        #endregion

        protected override void WorkerCompleted(RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                this.Title += " ERROR";
            }
        }

        protected override void WorkerDoWork(DoWorkEventArgs e)
        {
            var sw = new Stopwatch();
            int prevPercentage = 0;
            OperationStatus status = OperationStatus.Success;

            sw.Start();

            int hash = Guid.NewGuid().GetHashCode();
            double d = new Random(hash).NextDouble();
            if (d >= 0.5)
                throw new Exception("Testing");

            while (sw.ElapsedMilliseconds < _workTimeMS)
            {
                if (_cancel)
                {
                    status = OperationStatus.Canceled;
                    break;
                }

                System.Threading.Thread.Sleep(100);

                if (this.IsPaused || this.IsQueued)
                {
                    if (sw.IsRunning)
                        sw.Stop();
                    continue;
                }
                else if (this.IsWorking)
                {
                    if (!sw.IsRunning)
                        sw.Start();
                }

                int percentage = (int)Math.Round(((decimal)sw.ElapsedMilliseconds / (decimal)_workTimeMS) * 100);

                if (percentage != prevPercentage)
                {
                    prevPercentage = percentage;

                    this.ETA = (int)TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds).TotalSeconds + "s";
                    this.Progress = percentage;
                    this.ReportProgress(percentage, null);
                }
            }

            e.Result = status;
        }

        protected override void WorkerProgressChanged(ProgressChangedEventArgs e)
        {
            if (e.UserState == null)
                return;

            // Used to set multiple properties
            if (e.UserState is Dictionary<string, object>)
            {
                foreach (KeyValuePair<string, object> pair in (e.UserState as Dictionary<string, object>))
                {
                    this.GetType().GetProperty(pair.Key).SetValue(this, pair.Value);
                }
            }
        }
    }
}