﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using YouTube_Downloader_DLL.Classes;
using YouTube_Downloader_DLL.FFmpeg;

namespace YouTube_Downloader_DLL.Operations
{
    public class CroppingOperation : Operation
    {
        TimeSpan _start = TimeSpan.MinValue;
        TimeSpan _end = TimeSpan.MinValue;
        Process _process;
        CancellationTokenSource _cts = new CancellationTokenSource();

        public CroppingOperation(string input,
                                 string output,
                                 TimeSpan start,
                                 TimeSpan end)
        {
            this.ReportsProgress = true;
            this.Input = input;
            this.Output = output;

            _start = start;
            _end = end;

            this.Duration = (long)FFmpegProcess.GetDuration(this.Input).Value.TotalSeconds;
            this.Title = Path.GetFileName(this.Output);
            this.ProgressText = "Cropping...";
        }

        #region Operation members

        public override void Dispose()
        {
            base.Dispose();

            _process?.Dispose();
            _process = null;
        }

        public override bool CanOpen()
        {
            return this.IsSuccessful;
        }

        public override bool CanStop()
        {
            return this.IsWorking || this.IsQueued;
        }

        public override bool Open()
        {
            try
            {
                Process.Start(this.Output);
            }
            catch
            {
                return false;
            }
            return true;
        }

        public override bool OpenContainingFolder()
        {
            try
            {
                Process.Start(Path.GetDirectoryName(this.Output));
            }
            catch
            {
                return false;
            }
            return true;
        }

        public override bool Stop()
        {
            if (this.IsPaused || this.IsWorking || this.IsQueued)
            {
                try
                {
                    _cts?.Cancel();
                    this.CancelAsync();
                    this.Status = OperationStatus.Canceled;
                }
                catch (Exception ex)
                {
                    Common.SaveException(ex);
                    return false;
                }
            }

            if (!this.IsSuccessful)
            {
                Helper.DeleteFiles(this.Output);
            }

            return true;
        }

        #endregion

        protected override void WorkerCompleted(RunWorkerCompletedEventArgs e)
        {
            if (this.IsSuccessful)
            {
                this.Duration = (long)FFmpegProcess.GetDuration(this.Input).Value.TotalSeconds;
                this.FileSize = Helper.GetFileSize(this.Output);
            }
        }

        protected override void WorkerDoWork(DoWorkEventArgs e)
        {
            try
            {
                using (var logger = OperationLogger.Create(OperationLogger.FFmpegDLogFile))
                {
                    var ffmpeg = new FFmpegProcess(logger);

                    if (_end == TimeSpan.MinValue)
                        ffmpeg.Crop(this.Input, this.Output, _start, this.ReportProgress, _cts.Token);
                    else
                        ffmpeg.Crop(this.Input, this.Output, _start, _end, this.ReportProgress, _cts.Token);
                }

                _start = _end = TimeSpan.MinValue;

                e.Result = this.CancellationPending ? OperationStatus.Canceled : OperationStatus.Success;
            }
            catch (Exception ex)
            {
                Common.SaveException(ex);
                Helper.DeleteFiles(this.Output);
                e.Result = OperationStatus.Failed;
            }
        }

        protected override void WorkerProgressChanged(ProgressChangedEventArgs e)
        {
            if (e.UserState is Process)
            {
                // FFmpegHelper will return the ffmpeg process so it can be used to cancel.
                this._process = (Process)e.UserState;
            }
        }
    }
}
