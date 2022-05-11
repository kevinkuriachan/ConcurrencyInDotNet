using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebCrawler.WebCrawler
{
    public class WebCrawlerPC : IWebCrawler
    {
        private int _numThreads = 0;

        private SemaphoreSlim _emptySema;
        private SemaphoreSlim _fullSema = new SemaphoreSlim(0);

        private ManualResetEventSlim _quitEvent = new ManualResetEventSlim();
        private ManualResetEventSlim _proucerDoneEvent = new ManualResetEventSlim();

        private ConcurrentQueue<string> _urlsQueue = new ConcurrentQueue<string>();
        private ConcurrentDictionary<string, byte> _uniqueUrls = new ConcurrentDictionary<string, byte>();
        private HashSet<IPAddress> _uniqueIps = new HashSet<IPAddress>();

        private object _lockUniqueIps = new object();

        private string _filename;
        private Stopwatch _stopwatch = new Stopwatch();

        private long _totalSites = 0;
        private long _totalUniqueSites = 0;
        private long _totalResonsiveSites = 0;
        private long _totalBytesDownloaded = 0;

        private long _totalUnfoundHosts = 0;

        public WebCrawlerPC(string filename, int threads)
        {
            this._numThreads = threads;
            this._emptySema = new SemaphoreSlim(threads);
            this._filename = filename;
        }

        private async Task WorkerThread()
        {
            WaitHandle[] waits = { _quitEvent.WaitHandle, _proucerDoneEvent.WaitHandle, _fullSema.AvailableWaitHandle };

            bool producerDone = false;

            while (true)
            {
                int signal = await Task.Run(() => WaitHandle.WaitAny(waits));
                if (signal == 0)
                {
                    // quit 
                    break;
                }

                if (signal == 1)
                {
                    producerDone = true;
                }

                if (producerDone && _urlsQueue.IsEmpty)
                {
                    // quit
                    _quitEvent.Set();
                    break;
                }

                // aquired the _fullSema 
                // pop from queue and do work

                if (!_urlsQueue.TryDequeue(out string url))
                {
                    throw new Exception("unable to dequeue from queue");
                }
                // spot opened up in the queue 
                _emptySema.Release();

                await CheckUrlAsync(url);

            }
        }

        public async Task<WebCrawlResult> CrawlAsync(int? maxSites = null)
        {
            _stopwatch.Start();

            if (!File.Exists(_filename))
            {
                throw new ArgumentException("File does not exist");
            }

            // start worker threads 
            Task[] threads = new Task[_numThreads];
            for (int i = 0; i < _numThreads; i++)
            {
                threads[i] = Task.Run(() => WorkerThread());
            }

            using (StreamReader sr = File.OpenText(_filename))
            {
                string line = String.Empty;
                TimeSpan prevTime = _stopwatch.Elapsed;
                while ((line = sr.ReadLine()) != null)
                {
                    _totalSites++;
                    if (_totalSites % 1000 == 0)
                    {
                        TimeSpan timeNow = _stopwatch.Elapsed;
                        Console.WriteLine($"Reader: Read {_totalSites} lines -- Time: {timeNow.ToString()} -- Time Since Last Reader print: {(timeNow - prevTime).ToString()}");
                        prevTime = timeNow;
                    }


                    await _emptySema.WaitAsync();
                    _urlsQueue.Enqueue(line);
                    _fullSema.Release();


                    if (maxSites != null && _totalSites >= maxSites)
                    {
                        Console.WriteLine($"Reader: reached max lines to read of {maxSites}");
                        break;
                    }
                }

                _proucerDoneEvent.Set();

                await Task.WhenAll(threads);

                Console.WriteLine("Waiting for tasks to finish");
                    
            }


            _stopwatch.Stop();

            return new WebCrawlResult
            {
                //TotalSites = _totalSites,
                //TotalUniqueSites = _totalUniqueSites,
                //TotalUnfound = _totalUnfoundHosts,
                //TotalResponsiveSites = _totalResonsiveSites,
                //TotalBytesDownloaded = _totalBytesDownloaded,
                TotalTime = _stopwatch.Elapsed
            };
        }

        private async Task CheckUrlAsync(string url)
        {
            WebClient client = new WebClient();

            if (_uniqueUrls.ContainsKey(url))
            {
                return;
            }

            TimeSpan prevTime = _stopwatch.Elapsed;

            try
            {
                Uri uri;
                try
                {
                    uri = new Uri(url);
                }
                catch (Exception ex)
                {
                    return;
                }

                IPHostEntry dnsResult = await Dns.GetHostEntryAsync(uri.Authority);

                long prevUniqueIps = 0;
                long newUniqueIps = 0;


                if (dnsResult.AddressList.Length > 0)
                {
                    lock(_lockUniqueIps)
                    {
                        prevUniqueIps = _uniqueIps.Count;

                        foreach (IPAddress ip in dnsResult.AddressList)
                        {
                            _uniqueIps.Add(ip);
                        }

                        newUniqueIps = _uniqueIps.Count;
                    }
                }

                // a new unique ip was found 
                if (newUniqueIps != prevUniqueIps)
                {
                    long currUnique = Interlocked.Increment(ref _totalUniqueSites);

                    if (currUnique % 1000 == 0)
                    {
                        TimeSpan currTime = _stopwatch.Elapsed;
                        Console.WriteLine($"Worker: Downloaded from {currUnique} sites -- Time: {currTime} -- Time Since Last Worker Print: {(currTime - prevTime).ToString()}");
                        prevTime = currTime;
                    }

                    byte[] result = await client.DownloadDataTaskAsync(new Uri(url));
                    Interlocked.Add(ref _totalBytesDownloaded, result.LongLength);
                    Interlocked.Increment(ref _totalResonsiveSites);
                    //_totalUniqueSites++;
                    //_totalBytesDownloaded += result.LongLength;
                    return;
                }
            }
            catch (SocketException ex)
            {
                if (ex.ErrorCode == (int)SocketError.HostNotFound)
                {
                    //_totalUnfoundHosts++;
                    Interlocked.Increment(ref _totalUnfoundHosts);
                }
                return;
            }
            catch (Exception ex)
            {
                return;
            }
        }
    }
}
