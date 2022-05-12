using System;
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
    public class WebCrawlerInterlockedAwaitAll : IWebCrawler
    {
        private string _filename;
        private long _totalSites = 0;
        private long _totalUniqueSites = 0;
        private long _totalResonsiveSites = 0;
        private long _totalBytesDownloaded = 0;

        private long _totalUnfoundHosts = 0;

        private long _maxBytesFromPage = 5000;
        private long _downloads = 0;

        private long _totalPopped = 0;
        private long _totalComplete = 0;
        private long _threadsRunning = 0;
        private long _threadsWaiting = 0;

        private long _unsuccessful = 0;

        private Stopwatch _stopwatch = new Stopwatch();

        private readonly object _mutexUrls = new object();
        private readonly object _mutexIps = new object();

        private ManualResetEventSlim _quitEvent = new ManualResetEventSlim();

        private HashSet<string> _uniqueUrls = new HashSet<string>();
        private HashSet<IPAddress> _uniqueIps = new HashSet<IPAddress>();

        public WebCrawlerInterlockedAwaitAll(string filename)
        {
            _filename = filename;
        }

        private async Task CheckUrlAsync(string url)
        {
            HttpClient httpClient = new HttpClient();

            Interlocked.Increment(ref _threadsRunning);

            lock(_mutexUrls)
            {
                if (_uniqueUrls.Contains(url))
                {
                    Interlocked.Decrement(ref _threadsRunning);
                    Interlocked.Increment(ref _totalComplete);
                    return;
                }
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
                    Interlocked.Decrement(ref _threadsRunning);
                    Interlocked.Increment(ref _totalComplete);
                    return;
                }

                IPHostEntry dnsResult = await Dns.GetHostEntryAsync(uri.Authority);

                long prevUniqueIps = 0;
                long newUniqueIps = 0;

                lock (_mutexIps)
                {
                    prevUniqueIps = _uniqueIps.Count;

                    if (dnsResult.AddressList.Length > 0)
                    {
                        foreach (IPAddress ip in dnsResult.AddressList)
                        {
                            _ = _uniqueIps.Add(ip);
                        }
                    }

                    newUniqueIps = _uniqueIps.Count;
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
                        Interlocked.Decrement(ref _threadsRunning);
                        Interlocked.Increment(ref _totalComplete);
                        return;
                    }

                    Interlocked.Increment(ref _totalResonsiveSites);

                    if (checkReply.Content.Headers.ContentLength > _maxBytesFromPage)
                    {
                        Interlocked.Increment(ref _unsuccessful);
                        Interlocked.Decrement(ref _threadsRunning);
                        Interlocked.Increment(ref _totalComplete);
                        return;
                    }

                    byte[] result = await httpClient.GetByteArrayAsync(url);
                    Interlocked.Add(ref _totalBytesDownloaded, result.LongLength);

                    Interlocked.Increment(ref _downloads);
                    //_totalUniqueSites++;
                    //_totalBytesDownloaded += result.LongLength;
                    Interlocked.Decrement(ref _threadsRunning);
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
                Interlocked.Decrement(ref _threadsRunning);
                Interlocked.Increment(ref _totalComplete);
                return;
            }
            catch (Exception ex)
            {
                Interlocked.Decrement(ref _threadsRunning);
                Interlocked.Increment(ref _totalComplete);
                return;
            }
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
                Console.WriteLine($"Stats: Total Sites: {_totalSites} -- Running {_threadsRunning} -- Elapsed {_stopwatch.Elapsed.ToString()} -- Complete {_totalComplete} -- Downloads {_downloads} -- Responses {_totalResonsiveSites} -- Bytes {_totalBytesDownloaded}");
            }
        }

        public async Task<WebCrawlResult> CrawlAsync(int? maxSites = null)
        {
            List<Task> tasks = new List<Task>();

            _stopwatch.Start();

            if (File.Exists(_filename))
            {
                Task statsTread = Task.Run(() => StatsThread());
                using (StreamReader sr = File.OpenText(_filename))
                {
                    string line = String.Empty;
                    //TimeSpan prevTime = _stopwatch.Elapsed;
                    while ((line = sr.ReadLine()) != null)
                    {
                        _totalSites++;
                        tasks.Add(CheckUrlAsync(line));
                        if (maxSites != null && _totalSites >= maxSites)
                        {
                            Console.WriteLine($"Reader: reached max lines to read of {maxSites}");
                            break;
                        }
                    }

                }
                Console.WriteLine("Waiting for tasks to finish");
                await Task.WhenAll(tasks);
                _quitEvent.Set();
                await statsTread;
            }
            else
            {
                throw new ArgumentException("File does not exist");
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
    }
}
