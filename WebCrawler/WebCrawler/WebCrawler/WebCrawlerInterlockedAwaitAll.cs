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
                    Interlocked.Increment(ref _totalUniqueSites);
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
        public async Task<WebCrawlResult> CrawlAsync()
        {
            List<Task> tasks = new List<Task>();

            Stopwatch stopwatch = Stopwatch.StartNew();

            if (File.Exists(_filename))
            {
                using (StreamReader openFile = new System.IO.StreamReader(_filename))
                {
                    string line = string.Empty;
                    while ((line = openFile.ReadLine()) != null)
                    {
                        _totalSites++;
                        tasks.Add(CheckUrlAsync(line));
                    }

                    await Task.WhenAll(tasks);
                }
            }
            else
            {
                throw new ArgumentException("File does not exist");
            }

            stopwatch.Stop();

            return new WebCrawlResult
            {
                TotalSites = _totalSites,
                TotalUniqueSites = _totalUniqueSites,
                TotalUnfound = _totalUnfoundHosts,
                TotalResponsiveSites = _totalResonsiveSites,
                TotalBytesDownloaded = _totalBytesDownloaded,
                TotalTime = stopwatch.Elapsed
            };
        }
    }
}
