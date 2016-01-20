﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using YouTube_Downloader_DLL.Classes;

namespace YouTube_Downloader_DLL.Operations
{
    public class TwitchOperation : Operation
    {
        bool _processing = false;
        VideoFormat _format;
        CancellationTokenSource _cts;

        public TwitchOperation(VideoFormat format)
        {
            this.Duration = format.VideoInfo.Duration;
            this.FileSize = format.FileSize;
            this.Link = format.VideoInfo.Url;
            this.ReportsProgress = true;
            this.Thumbnail = format.VideoInfo.ThumbnailUrl;
            this.Title = format.VideoInfo.Title;
        }

        #region Operation members

        public override void Dispose()
        {
            base.Dispose();

            _cts.Cancel();
            _cts.Dispose();
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

        public override bool Stop(bool cleanup)
        {
            _cts.Cancel();
            return true;
        }

        public override bool CanOpen()
        {
            return this.Status == OperationStatus.Success;
        }

        public override bool CanStop()
        {
            return this.Status == OperationStatus.Working;
        }

        #endregion

        protected override void WorkerCompleted(RunWorkerCompletedEventArgs e)
        {
            this.Status = (OperationStatus)e.Result;
        }

        protected override void WorkerDoWork(DoWorkEventArgs e)
        {
            _cts = new CancellationTokenSource();

            try
            {
                YoutubeDlHelper.DownloadTwitchVOD(this.Output, _format, delegate (TwitchOperationProgress progressUpdate)
                {
                    this.ReportProgress((int)progressUpdate.ProgressPercentage, progressUpdate);
                }, _cts.Token);

                // Make sure progress reaches 100%
                if (this.Progress < ProgressMax)
                    this.ReportProgress(ProgressMax, null);

                e.Result = OperationStatus.Success;
            }
            catch (OperationCanceledException)
            {
                e.Result = OperationStatus.Canceled;
            }
            catch (Exception ex)
            {
                Common.SaveException(ex);
                e.Result = OperationStatus.Failed;
            }
        }

        protected override void WorkerProgressChanged(ProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage >= 0)
                this.Progress = e.ProgressPercentage;

            if (e.UserState == null || _processing)
                return;

            if (e.UserState is TwitchOperationProgress)
            {
                _processing = true;

                TwitchOperationProgress progressUpdate = e.UserState as TwitchOperationProgress;

                this.ETA = progressUpdate.ETA;
                this.Speed = string.Format("{0} {1}/s", progressUpdate.Speed, progressUpdate.SpeedSuffix);

                _processing = false;
            }
        }

        protected override void WorkerStart(object[] args)
        {
            this.Output = (string)args[0];

            _format = (VideoFormat)args[1];
        }

        public static object[] Args(string output, VideoFormat format)
        {
            return new object[] { output, format };
        }
    }
}
