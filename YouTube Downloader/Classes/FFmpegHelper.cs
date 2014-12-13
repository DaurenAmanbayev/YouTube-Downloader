﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace YouTube_Downloader.Classes
{
    public class FFmpegHelper
    {
        private const string Cmd_Combine_Dash = " -y -i \"{0}\" -i \"{1}\" -vcodec copy -acodec copy \"{2}\"";
        /* Cmd_Convert options:
         *
         * -y  - Overwrite output file without asking
         * -i  - Input file name
         * -vn - Disables video recording.
         * -f  - Forces file format, but isn't needed if output has .mp3 extensions
         * -ab - Sets the audio bitrate
         */
        private const string Cmd_Convert = " -y -i \"{0}\" -vn -f mp3 -ab {1}k \"{2}\"";
        private const string Cmd_Crop_From = " -y -ss {0} -i \"{1}\" -acodec copy{2} \"{3}\"";
        private const string Cmd_Crop_From_To = " -y -ss {0} -i \"{1}\" -to {2} -acodec copy{3} \"{4}\"";
        private const string Cmd_Get_File_Info = " -i \"{0}\"";

        public enum FileType { Audio, Error, Video }

        /// <summary>
        /// Gets the path to FFmpeg executable.
        /// </summary>
        public static string FFmpegPath = Path.Combine(Application.StartupPath, "externals", "ffmpeg.exe");

        /// <summary>
        /// Returns true if given file can be converted to a MP3 file, false otherwise.
        /// </summary>
        /// <param name="file">The file to check.</param>
        public static bool CanConvertMP3(string file)
        {
            string arguments = string.Format(Cmd_Get_File_Info, file);

            Process process = StartProcess(arguments);

            string line = "";
            bool hasAudioStream = false;

            /* Write output to log. */
            using (var writer = CreateLogWriter())
            {
                WriteLogHeader(writer, arguments);

                while ((line = process.StandardError.ReadLine()) != null)
                {
                    writer.WriteLine(line);
                    line = line.Trim();

                    if (line.StartsWith("Stream #") && line.Contains("Audio"))
                    {
                        /* File has audio stream. */
                        hasAudioStream = true;
                    }
                }

                WriteLogFooter(writer);
            }

            process.WaitForExit();

            if (!process.HasExited)
                process.Kill();

            return hasAudioStream;
        }

        /// <summary>
        /// Returns true if given audio &amp; video file can be combined, false otherwise.
        /// </summary>
        /// <param name="audio">The input audio file.</param>
        /// <param name="video">The input video file.</param>
        public static List<string> CheckCombine(string audio, string video)
        {
            List<string> errors = new List<string>();
            string argsAudio = string.Format(Cmd_Get_File_Info, audio);
            string argsVideo = string.Format(Cmd_Get_File_Info, video);

            Process process;

            using (var writer = CreateLogWriter())
            {
                WriteLogHeader(writer, argsAudio);

                using (process = StartProcess(argsAudio))
                {
                    string line = "";
                    bool hasAudio = false;

                    while ((line = process.StandardError.ReadLine()) != null)
                    {
                        writer.WriteLine(line);
                        line = line.Trim();

                        if (line.StartsWith("major_brand"))
                        {
                            string value = line.Split(':')[1].Trim();

                            if (!value.Contains("dash"))
                            {
                                errors.Add("Audio doesn't appear to be a DASH file. Non-critical.");
                            }
                        }
                        else if (line.StartsWith("Stream #"))
                        {
                            if (line.Contains("Audio"))
                            {
                                hasAudio = true;
                            }
                            else if (line.Contains("Video"))
                            {
                                errors.Add("Audio file also has a video stream.");
                            }
                        }
                    }

                    if (!hasAudio)
                    {
                        errors.Add("Audio file doesn't audio.");
                    }
                }

                WriteLogFooter(writer);
            }

            return errors;
        }

        /// <summary>
        /// Opens a <see cref="System.IO.StreamWriter"/> for FFmpeg log file.
        /// </summary>
        private static StreamWriter CreateLogWriter()
        {
            string folder = Program.GetLogsDirectory();

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            StreamWriter writer = new StreamWriter(Path.Combine(folder, "ffmpeg.log"), true)
            {
                AutoFlush = true
            };

            return writer;
        }

        /// <summary>
        /// Combines DASH audio &amp; video to a single MP4 file.
        /// </summary>
        /// <param name="video">The input video file.</param>
        /// <param name="audio">The input audio file.</param>
        /// <param name="output">Where to save the output file.</param>
        public static void CombineDash(string video, string audio, string output)
        {
            string[] args = new string[] { video, audio, output };
            string arguments = string.Format(FFmpegHelper.Cmd_Combine_Dash, args);

            Process process = FFmpegHelper.StartProcess(arguments);

            string line = "";

            /* Write output to log. */
            using (var writer = CreateLogWriter())
            {
                WriteLogHeader(writer, arguments);

                while ((line = process.StandardError.ReadLine()) != null)
                {
                    writer.WriteLine(line);
                }

                WriteLogFooter(writer);
            }

            process.WaitForExit();

            if (!process.HasExited)
                process.Kill();
        }

        /// <summary>
        /// Converts file to MP3, reporting progress to given <see cref="System.ComponentModel.BackgroundWorker"/>.
        /// Possibly more formats in the future.
        /// </summary>
        /// <param name="bw">The <see cref="System.ComponentModel.BackgroundWorker"/> to report progress to. Can be null.</param>
        /// <param name="input">The input file.</param>
        /// <param name="output">Where to save the output file.</param>
        public static void Convert(BackgroundWorker bw, string input, string output)
        {
            if (input == output)
            {
                throw new Exception("Input & output can't be the same.");
            }

            string[] args = new string[] { input, GetBitRate(input).ToString(), output };
            string arguments = string.Format(FFmpegHelper.Cmd_Convert, args);

            Process process = FFmpegHelper.StartProcess(arguments);

            bw.ReportProgress(0, process);

            bool started = false;
            double milliseconds = 0;
            string line = "";

            /* Write output to log. */
            using (var writer = CreateLogWriter())
            {
                WriteLogHeader(writer, arguments);

                while ((line = process.StandardError.ReadLine()) != null)
                {
                    writer.WriteLine(line);

                    // 'bw' is null, don't report any progress
                    if (bw != null && bw.WorkerReportsProgress)
                    {
                        line = line.Trim();

                        if (line.StartsWith("Duration: "))
                        {
                            int start = "Duration: ".Length;
                            int length = "00:00:00.00".Length;

                            string time = line.Substring(start, length);

                            milliseconds = TimeSpan.Parse(time).TotalMilliseconds;
                        }
                        else if (line == "Press [q] to stop, [?] for help")
                        {
                            started = true;

                            bw.ReportProgress(0);
                        }
                        else if (started && line.StartsWith("size="))
                        {
                            int start = line.IndexOf("time=") + 5;
                            int length = "00:00:00.00".Length;

                            string time = line.Substring(start, length);

                            double currentMilli = TimeSpan.Parse(time).TotalMilliseconds;
                            double percentage = (currentMilli / milliseconds) * 100;

                            bw.ReportProgress(System.Convert.ToInt32(percentage));
                        }
                        else if (started && line == string.Empty)
                        {
                            started = false;

                            bw.ReportProgress(100);
                        }
                    }
                }

                WriteLogFooter(writer);
            }

            process.WaitForExit();

            if (!process.HasExited)
                process.Kill();
        }

        /// <summary>
        /// Crops file from given start position to end, reporting progress to given <see cref="System.ComponentModel.BackgroundWorker"/>.
        /// </summary>
        /// <param name="bw">The <see cref="System.ComponentModel.BackgroundWorker"/> to report progress to. Can be null.</param>
        /// <param name="input">The input file.</param>
        /// <param name="output">Where to save the output file.</param>
        /// <param name="start">The <see cref="System.TimeSpan"/> start position.</param>
        public static void Crop(BackgroundWorker bw, string input, string output, TimeSpan start)
        {
            if (input == output)
            {
                throw new Exception("Input & output can't be the same.");
            }

            string[] args = new string[]
            {
                string.Format("{0:00}:{1:00}:{2:00}.{3:000}", start.Hours, start.Minutes, start.Seconds, start.Milliseconds),
                input,
                GetFileType(input) == FileType.Video ? " -vcodec copy" : "",
                output
            };

            string arguments = string.Format(Cmd_Crop_From, args);

            Process process = FFmpegHelper.StartProcess(arguments);

            bw.ReportProgress(0, process);

            bool started = false;
            double milliseconds = 0;
            string line = "";

            while ((line = process.StandardError.ReadLine()) != null)
            {
                // 'bw' is null, don't report any progress
                if (bw != null && bw.WorkerReportsProgress)
                {
                    line = line.Trim();

                    if (line.StartsWith("Duration: "))
                    {
                        int lineStart = "Duration: ".Length;
                        int length = "00:00:00.00".Length;

                        string time = line.Substring(lineStart, length);

                        milliseconds = TimeSpan.Parse(time).TotalMilliseconds;
                    }
                    else if (line == "Press [q] to stop, [?] for help")
                    {
                        started = true;

                        bw.ReportProgress(0);
                    }
                    else if (started && line.StartsWith("size="))
                    {
                        int lineStart = line.IndexOf("time=") + 5;
                        int length = "00:00:00.00".Length;

                        string time = line.Substring(lineStart, length);

                        double currentMilli = TimeSpan.Parse(time).TotalMilliseconds;
                        double percentage = (currentMilli / milliseconds) * 100;

                        bw.ReportProgress(System.Convert.ToInt32(percentage));
                    }
                    else if (started && line == string.Empty)
                    {
                        started = false;

                        bw.ReportProgress(100);
                    }
                }
            }

            process.WaitForExit();

            if (!process.HasExited)
                process.Kill();
        }

        /// <summary>
        /// Crops file from given start position to given end position, reporting progress to given <see cref="System.ComponentModel.BackgroundWorker"/>.
        /// </summary>
        /// <param name="bw">The <see cref="System.ComponentModel.BackgroundWorker"/> to report progress to. Can be null.</param>
        /// <param name="input">The input file.</param>
        /// <param name="output">Where to save the output file.</param>
        /// <param name="start">The <see cref="System.TimeSpan"/> start position.</param>
        /// <param name="end">The <see cref="System.TimeSpan"/> end position.</param>
        public static void Crop(BackgroundWorker bw, string input, string output, TimeSpan start, TimeSpan end)
        {
            if (input == output)
            {
                throw new Exception("Input & output can't be the same.");
            }

            TimeSpan length = new TimeSpan((long)Math.Abs(start.Ticks - end.Ticks));

            string[] args = new string[]
            {
                string.Format("{0:00}:{1:00}:{2:00}.{3:000}", start.Hours, start.Minutes, start.Seconds, start.Milliseconds),
                input,
                string.Format("{0:00}:{1:00}:{2:00}.{3:000}", length.Hours, length.Minutes, length.Seconds, length.Milliseconds),
                GetFileType(input) == FileType.Video ? " -vcodec copy" : "",
                output
            };

            string arguments = string.Format(Cmd_Crop_From_To, args);

            Process process = FFmpegHelper.StartProcess(arguments);

            bw.ReportProgress(0, process);

            bool started = false;
            double milliseconds = 0;
            string line = "";

            while ((line = process.StandardError.ReadLine()) != null)
            {
                // 'bw' is null, don't report any progress
                if (bw != null && bw.WorkerReportsProgress)
                {
                    line = line.Trim();

                    milliseconds = end.TotalMilliseconds;

                    if (line == "Press [q] to stop, [?] for help")
                    {
                        started = true;

                        bw.ReportProgress(0);
                    }
                    else if (started && line.StartsWith("size="))
                    {
                        int lineStart = line.IndexOf("time=") + 5;
                        int lineLength = "00:00:00.00".Length;

                        string time = line.Substring(lineStart, lineLength);

                        double currentMilli = TimeSpan.Parse(time).TotalMilliseconds;
                        double percentage = (currentMilli / milliseconds) * 100;

                        bw.ReportProgress(System.Convert.ToInt32(percentage));
                    }
                    else if (started && line == string.Empty)
                    {
                        started = false;

                        bw.ReportProgress(100);
                    }
                }
            }

            process.WaitForExit();

            if (!process.HasExited)
                process.Kill();
        }

        public static int GetBitRate(string file)
        {
            int result = -1;
            string arguments = string.Format(" -i \"{0}\"", file);
            Process process = StartProcess(arguments);
            List<string> lines = new List<string>();

            // Read to EOS, storing each line
            while (!process.StandardError.EndOfStream)
                lines.Add(process.StandardError.ReadLine().Trim());

            foreach (string line in lines)
            {
                if (line.StartsWith("Stream"))
                {
                    Regex regex = new Regex(@"^Stream\s#\d:\d.*\s(\d+)\skb/s.*$");
                    Match m = regex.Match(line);

                    if (m.Success)
                        result = int.Parse(m.Groups[1].Value);
                }
            }

            process.WaitForExit();

            if (!process.HasExited)
                process.Kill();

            return result;
        }

        /// <summary>
        /// Returns the <see cref="System.TimeSpan"/> duration of the given file.
        /// </summary>
        /// <param name="file">The file to get <see cref="System.TimeSpan"/> duration from.</param>
        public static TimeSpan GetDuration(string file)
        {
            TimeSpan result = TimeSpan.Zero;
            string arguments = string.Format(" -i \"{0}\"", file);
            Process process = StartProcess(arguments);
            List<string> lines = new List<string>();

            // Read to EOS, storing each line.
            while (!process.StandardError.EndOfStream)
            {
                lines.Add(process.StandardError.ReadLine().Trim());
            }

            foreach (var line in lines)
            {
                // Example line, including whitespace:
                //  Duration: 00:00:00.00, start: 0.000000, bitrate: *** kb/s
                if (line.StartsWith("Duration"))
                {
                    string[] split = line.Split(' ', ',');

                    result = TimeSpan.Parse(split[1]);
                    break;
                }
            }

            process.WaitForExit();

            if (!process.HasExited)
                process.Kill();

            return result;
        }

        /// <summary>
        /// Returns the <see cref="FileType"/> of the given file.
        /// </summary>
        /// <param name="file">The file to get <see cref="FileType"/> from.</param>
        public static FileType GetFileType(string file)
        {
            FileType result = FileType.Error;
            string arguments = string.Format(" -i \"{0}\"", file);
            Process process = StartProcess(arguments);
            List<string> lines = new List<string>();

            // Read to EOS, storing each line.
            while (!process.StandardError.EndOfStream)
            {
                lines.Add(process.StandardError.ReadLine().Trim());
            }

            foreach (var line in lines)
            {
                // Example lines, including whitespace:
                //    Stream #0:0(und): Video: h264 ([33][0][0][0] / 0x0021), yuv420p, 320x240 [SAR 717:716 DAR 239:179], q=2-31, 242 kb/s, 29.01 fps, 90k tbn, 90k tbc (default)
                //    Stream #0:1(eng): Audio: vorbis ([221][0][0][0] / 0x00DD), 44100 Hz, stereo (default)
                if (line.StartsWith("Stream #"))
                {
                    if (line.Contains("Video: "))
                    {
                        // File contains video stream, so it's a video file, possibly without audio.
                        result = FileType.Video;
                        break;
                    }
                    else if (line.Contains("Audio: "))
                    {
                        // File contains audio stream. Keep looking for a video stream,
                        // and if found it's probably a video file, and an audio file if not.
                        result = FileType.Audio;
                    }
                }
            }

            process.WaitForExit();

            if (!process.HasExited)
                process.Kill();

            return result;
        }

        /// <summary>
        /// Creates a Process with the given arguments, then returns it after it has started.
        /// </summary>
        /// <param name="arguments">The process arguments.</param>
        private static Process StartProcess(string arguments)
        {
            Process process = new Process();

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.FileName = FFmpegHelper.FFmpegPath;
            process.StartInfo.Arguments = arguments;
            process.Start();

            return process;
        }

        /// <summary>
        /// Writes log footer to <see cref="StreamWriter"/>.
        /// </summary>
        /// <param name="writer">The <see cref="StreamWriter"/> to write to.</param>
        private static void WriteLogFooter(StreamWriter writer)
        {
            // Write log footer to stream.
            // Possibly write elapsed time and/or error in future.
            writer.WriteLine();
            writer.WriteLine();
            writer.WriteLine();
        }

        /// <summary>
        /// Writes log header to <see cref="StreamWriter"/>.
        /// </summary>
        /// <param name="writer">The <see cref="StreamWriter"/> to write to.</param>
        /// <param name="arguments">The arguments to log in header.</param>
        private static void WriteLogHeader(StreamWriter writer, string arguments)
        {
            // Write log header to stream
            writer.WriteLine("[" + DateTime.Now + "]");
            writer.WriteLine("cmd: " + arguments);
            writer.WriteLine();
            writer.WriteLine("OUTPUT");
        }
    }
}
