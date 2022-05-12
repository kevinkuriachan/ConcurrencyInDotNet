using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
        private SemaphoreSlim _fullSema;

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
        private long _unsuccessful = 0;

        private long _totalPopped = 0;
        private long _totalComplete = 0;
        private long _threadsRunning = 0;
        private long _threadsWaiting = 0;

        public WebCrawlerPC(string filename, int threads)
        {
            this._numThreads = threads;
            this._emptySema = new SemaphoreSlim(threads, threads);
            this._fullSema = _fullSema = new SemaphoreSlim(0, threads);
            this._filename = filename;
        }

        private async Task StatsThread()
        {
            while (true)
            {
                if (_quitEvent.Wait(2000))
                {
                    return;
                }
                //await Task.Delay(2000);
                Console.WriteLine($"Stats: Waiting {_threadsWaiting} -- Running {_threadsRunning} -- Queued {_urlsQueue.Count} -- Elapsed {_stopwatch.Elapsed.ToString()} -- Popped {_totalPopped} -- Complete {_totalComplete} -- Responses {_totalResonsiveSites} -- Bytes {_totalBytesDownloaded}");
            }
        }

        private async Task WorkerThread()
        {
            WaitHandle[] waits = { _quitEvent.WaitHandle, _proucerDoneEvent.WaitHandle, _fullSema.AvailableWaitHandle };

            bool producerDone = false;

            WebClient client = new WebClient();
            HttpClient httpClient = new HttpClient();

            while (true)
            {
                Interlocked.Increment(ref _threadsWaiting);
                int signal = WaitHandle.WaitAny(waits);
                if (signal == 0)
                {
                    // quit 
                    return;
                }

                if (signal == 1)
                {
                    producerDone = true;
                }

                if (producerDone && _urlsQueue.IsEmpty)
                {
                    // no more work, quit
                    return;
                }

                await _fullSema.WaitAsync();
                Interlocked.Increment(ref _threadsRunning);
                Interlocked.Decrement(ref _threadsWaiting);

                // aquired the _fullSema 
                // pop from queue and do work

                if (!_urlsQueue.TryDequeue(out string url))
                {
                    throw new Exception("unable to dequeue from queue");
                }
                // spot opened up in the queue 
                _emptySema.Release();

                Interlocked.Increment(ref _totalPopped);

                await CheckUrlAsync(url, client, httpClient);

                Interlocked.Increment(ref _totalComplete);
                long threadsRemaining = Interlocked.Decrement(ref _threadsRunning);

                if (_proucerDoneEvent.IsSet && _urlsQueue.IsEmpty && threadsRemaining == 0)
                {
                    _quitEvent.Set();
                }

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
            Task[] threads = new Task[_numThreads+1];
            threads[_numThreads] = Task.Run(() => StatsThread());
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
                Console.WriteLine("Waiting for tasks to finish");

                await Task.WhenAll(threads);
            }


            _stopwatch.Stop();

            return new WebCrawlResult
            {
                TotalSites = _totalSites,
                TotalUniqueSites = _totalUniqueSites,
                TotalUnfound = _totalUnfoundHosts,
                TotalResponsiveSites = _totalResonsiveSites,
                TotalBytesDownloaded = _totalBytesDownloaded,
                TotalTime = _stopwatch.Elapsed
            };
        }

        private async Task CheckUrlAsync(string url, WebClient? clientToUse = null, HttpClient? httpClientToUse = null)
        {
            WebClient client = clientToUse ?? new WebClient();
            HttpClient httpClient = httpClientToUse ?? new HttpClient();

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

                    HttpResponseMessage checkReply = await httpClient.GetAsync(url);

                    if (!checkReply.IsSuccessStatusCode)
                    {
                        Interlocked.Increment(ref _unsuccessful);
                        return;
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
