using System;
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
    public class WebCrawlerInterlockedAwaitAll : IWebCrawler
    {
        private string _filename;
        private long _totalSites = 0;
        private long _totalUniqueSites = 0;
        private long _totalResonsiveSites = 0;
        private long _totalBytesDownloaded = 0;

        private long _totalUnfoundHosts = 0;

        private Stopwatch _stopwatch = new Stopwatch();

        private readonly object _mutexUrls = new object();
        private readonly object _mutexIps = new object();

        private HashSet<string> _uniqueUrls = new HashSet<string>();
        private HashSet<IPAddress> _uniqueIps = new HashSet<IPAddress>();

        public WebCrawlerInterlockedAwaitAll(string filename)
        {
            _filename = filename;
        }

        private async Task CheckUrlAsync(string url)
        {
            WebClient client = new WebClient();

            lock(_mutexUrls)
            {
                if (_uniqueUrls.Contains(url))
                {
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
        public async Task<WebCrawlResult> CrawlAsync(int? maxSites = null)
        {
            List<Task> tasks = new List<Task>();

            _stopwatch.Start();

            if (File.Exists(_filename))
            {
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
                        tasks.Add(CheckUrlAsync(line));
                        if (maxSites != null && _totalSites >= maxSites)
                        {
                            Console.WriteLine($"Reader: reached max lines to read of {maxSites}");
                            break;
                        }
                    }

                    Console.WriteLine("Waiting for tasks to finish");
                    await Task.WhenAll(tasks);
                }
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
