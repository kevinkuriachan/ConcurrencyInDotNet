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
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WebCrawler.WebCrawler
{
    public class WebCrawlerChannel : IWebCrawler
    {
        private int _numThreads = 0;

        private ManualResetEventSlim _quitEvent = new ManualResetEventSlim();

        private Channel<string> _channel;

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

        private long _downloads = 0;

        private long _maxBytesFromPage = 5000;

        private long _remainingThreads = 0;

        public WebCrawlerChannel(string filename, int threads)
        {
            this._numThreads = threads;
            this._channel = Channel.CreateBounded<string>(new BoundedChannelOptions(_numThreads)
            {
                FullMode = BoundedChannelFullMode.Wait
            });
            this._filename = filename;
        }

        private async Task StatsThread()
        {
            while (true)
            {
                if (_quitEvent.Wait(2000))
                {
                    Console.WriteLine("Quitting stats");
                    return;
                }
                //await Task.Delay(2000);
                Console.WriteLine($"Stats: Waiting on threads {_remainingThreads} -- Running {_threadsRunning} -- Queued {_channel.Reader.Count} -- Elapsed {_stopwatch.Elapsed.ToString()} -- Popped {_totalPopped} -- Complete {_totalComplete} -- Downloads {_downloads} -- Responses {_totalResonsiveSites} -- Bytes {_totalBytesDownloaded}");
            }
        }

        private async Task WorkerThread()
        {
            
            HttpClient httpClient = new HttpClient();

            Interlocked.Increment(ref _remainingThreads);

            while (true)
            {
                while (await _channel.Reader.WaitToReadAsync())
                {
                    Interlocked.Increment(ref _threadsRunning);

                    while (_channel.Reader.TryRead(out string url))
                    {
                        Interlocked.Increment(ref _totalPopped);

                        await CheckUrlAsync(url, httpClient);
                    }
                    Interlocked.Decrement(ref _threadsRunning);
                }

                long threadsRemaining = Interlocked.Decrement(ref _remainingThreads);

                if (threadsRemaining == 0)
                {
                    _quitEvent.Set();
                    return;
                }

                return;
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
            Task[] threads = new Task[_numThreads + 1];
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

                    await _channel.Writer.WriteAsync(line);


                    if (maxSites != null && _totalSites >= maxSites)
                    {
                        Console.WriteLine($"Reader: reached max lines to read of {maxSites}");
                        break;
                    }
                }

                _channel.Writer.Complete();
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

        private async Task CheckUrlAsync(string url, HttpClient? httpClientToUse = null)
        {
            HttpClient httpClient = httpClientToUse ?? new HttpClient();

            if (_uniqueUrls.ContainsKey(url))
            {
                Interlocked.Increment(ref _totalComplete);
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
                    Interlocked.Increment(ref _totalComplete);
                    return;
                }

                IPHostEntry dnsResult = await Dns.GetHostEntryAsync(uri.Authority);

                long prevUniqueIps = 0;
                long newUniqueIps = 0;


                if (dnsResult.AddressList.Length > 0)
                {
                    lock (_lockUniqueIps)
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

                    HttpRequestMessage headReq = new HttpRequestMessage();
                    headReq.Method = HttpMethod.Head;
                    headReq.RequestUri = uri;

                    HttpResponseMessage checkReply = await httpClient.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead);

                    if (!checkReply.IsSuccessStatusCode)
                    {
                        Interlocked.Increment(ref _unsuccessful);
                        Interlocked.Increment(ref _totalComplete);
                        return;
                    }

                    Interlocked.Increment(ref _totalResonsiveSites);

                    if (checkReply.Content.Headers.ContentLength > _maxBytesFromPage)
                    {
                        Interlocked.Increment(ref _unsuccessful);
                        Interlocked.Increment(ref _totalComplete);
                        return;
                    }

                    byte[] result = await httpClient.GetByteArrayAsync(url);
                    Interlocked.Add(ref _totalBytesDownloaded, result.LongLength);

                    Interlocked.Increment(ref _downloads);
                    //_totalUniqueSites++;
                    //_totalBytesDownloaded += result.LongLength;
                    Interlocked.Increment(ref _totalComplete);
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
                Interlocked.Increment(ref _totalComplete);
                return;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _totalComplete);
                return;
            }
        }
    }
}
