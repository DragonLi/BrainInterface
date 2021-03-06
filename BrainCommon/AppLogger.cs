﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

#if !DEBUG
using System.IO.Compression;
#endif

namespace BrainCommon
{
    /// <summary>
    /// 日志管理器，第一次使用自动使用日期创建日志文件，
    /// release模式编译支持压缩存储日志
    /// </summary>
    public static class AppLogger
    {
        private static readonly StreamWriter LogFile;
#if DEBUG
        private const string logfilepostfix = ".log";
#else
        private const string logfilepostfix = ".log.gz";
#endif
        
        static AppLogger()
        {
            var utcNow = DateTime.UtcNow;
            var logFn = $"{utcNow.Year}.{utcNow.Month}.{utcNow.Day}.{utcNow.Ticks}{logfilepostfix}";
            try
            {
                Stream baseStream = File.Create(logFn);
#if !DEBUG
                baseStream = new GZipStream(baseStream,CompressionMode.Compress);
#endif
                LogFile = new StreamWriter(baseStream);
                LogFile.AutoFlush = true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                LogFile = new StreamWriter(new MemoryStream());
            }
            Logqueue = new ConcurrentQueue<Tuple<string, string>>();
            AppDomain.CurrentDomain.ProcessExit += ProcessExit;
            AppDomain.CurrentDomain.UnhandledException += LogUnhandledException;
        }

        private static void LogUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Error(e.ExceptionObject.ToString());
        }

        public static void ProcessExit(object sender, EventArgs e)
        {
            while (!Logqueue.IsEmpty)
            {
                if (_wrintingTask != null && !_wrintingTask.IsCompleted)
                {
                    _wrintingTask.Wait();
                }
                else
                {
                    StartWrite();
                }
            }
            if (_wrintingTask != null && !_wrintingTask.IsCompleted)
            {
                _wrintingTask.Wait();
            }
            LogFile?.Close();
        }

        private static readonly ConcurrentQueue<Tuple<string, string>> Logqueue;
        private static int _isWriting = CASHelper.LockFree;
        private static Task _wrintingTask;
        
        private static void StartWrite()
        {
            var isWriting = Interlocked.Exchange(ref _isWriting, CASHelper.LockUsed);
            if (isWriting != CASHelper.LockUsed)
            {
                if (Logqueue.TryDequeue(out var tuple))
                    _wrintingTask = Task.Factory.StartNew(() =>
                    {
                        do
                        {
                            LogFile.Write(tuple.Item1);
                            LogFile.WriteLine(tuple.Item2);
                            LogFile.Flush();
                        } while (Logqueue.TryDequeue(out tuple));
                        Interlocked.Exchange(ref _isWriting, CASHelper.LockFree);
                    });
                else
                {
                    Interlocked.Exchange(ref _isWriting, CASHelper.LockFree);
                }
            }
        }

        public static void Error(string log)
        {
            var dateTime = DateTime.Now;
            Logqueue.Enqueue(Tuple.Create($"{dateTime},{dateTime.Ticks},Error,", log));
            StartWrite();
            Console.WriteLine("error," + log);
        }

        public static void Warning(string log)
        {
            var dateTime = DateTime.Now;
            Logqueue.Enqueue(Tuple.Create($"{dateTime},{dateTime.Ticks},Warn,", log));
            StartWrite();
            Console.WriteLine("warn," + log);
        }

        public static void Info(string log)
        {
            var dateTime = DateTime.Now;
            Logqueue.Enqueue(Tuple.Create($"{dateTime},{dateTime.Ticks},Info,", log));
            StartWrite();
            Console.WriteLine("info," + log);
        }

        public static void Debug(string log)
        {
            var dateTime = DateTime.Now;
            Logqueue.Enqueue(Tuple.Create($"{dateTime},{dateTime.Ticks},Debug,", log));
            StartWrite();
            Console.WriteLine("debug," + log);
        }

        public static void Debug(object log)
        {
            Debug(log.ToString());
        }
    }
}