using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace WebCrawler.WebCrawler
{
    public class WebCrawlerAwaitAll : IWebCrawler
    {
        internal class SingleUrlResult
        {
            public bool Valid { get; set; }
            public bool Unique { get; set; }
            public bool Response { get; set; }
            public bool Unfound { get; set; }
            public long Bytes { get; set; }
        }

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

        public WebCrawlerAwaitAll(string filename)
        {
            _filename = filename;
        }

        private async Task<SingleUrlResult> CheckUrlAsync(string url)
        {
            WebClient client = new WebClient();

            lock(_mutexUrls)
            {
                if (_uniqueUrls.Contains(url))
                {
                    return new SingleUrlResult
                    {
                        Valid = true,
                        Unique = false,
                        Response = false,
                        Unfound = false,
                        Bytes = 0
                    };
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
                    return new SingleUrlResult
                    {
                        Valid = false,
                        Unique = false,
                        Response = false,
                        Unfound = false,
                        Bytes = 0
                    };
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
                    byte[] result = await client.DownloadDataTaskAsync(new Uri(url));
                    //_totalUniqueSites++;
                    //_totalBytesDownloaded += result.LongLength;
                    return new SingleUrlResult
                    {
                        Valid = true,
                        Unique = true,
                        Response = true,
                        Unfound = false,
                        Bytes = result.LongLength
                    };
                }

                return new SingleUrlResult
                {
                    Valid = true,
                    Unique = false,
                    Response = false,
                    Unfound = false,
                    Bytes = 0
                };
            }
            catch (SocketException ex)
            {
                if (ex.ErrorCode == (int)SocketError.HostNotFound)
                {
                    //_totalUnfoundHosts++;
                    return new SingleUrlResult
                    {
                        Valid = true,
                        Unique = true,
                        Response = false,
                        Unfound = true,
                        Bytes = 0
                    };
                }
                return new SingleUrlResult
                {
                    Valid = true,
                    Unique = true,
                    Response = false,
                    Unfound = false,
                    Bytes = 0
                };
            }
            catch (Exception ex)
            {
                return new SingleUrlResult
                {
                    Valid = true,
                    Unique = false,
                    Response = false,
                    Unfound = false,
                    Bytes = 0
                };
            }
        }
        public async Task<WebCrawlResult> CrawlAsync()
        {
            List<Task<SingleUrlResult>> tasks = new List<Task<SingleUrlResult>>();

            Stopwatch stopwatch = Stopwatch.StartNew();

            if (File.Exists(_filename))
            {
                using (StreamReader openFile = new System.IO.StreamReader(_filename))
                {
                    string line = string.Empty;
                    while ((line = openFile.ReadLine()) != null)
                    {
                        tasks.Add(CheckUrlAsync(line));
                    }

                    SingleUrlResult[] results = await Task.WhenAll(tasks);
                    _totalSites = results.AsParallel()
                                        .Where(r => r.Valid)
                                        .Count();

                    _totalUniqueSites = results.AsParallel()
                                        .Where(r => r.Unique)
                                        .Count();

                    _totalResonsiveSites = results.AsParallel()
                                            .Where(r => r.Response)
                                            .Count();

                    _totalUnfoundHosts = results.AsParallel()
                                            .Where(r => r.Unfound)
                                            .Count();

                    _totalBytesDownloaded = results.AsParallel()
                                            .Where(r => r.Bytes > 0)
                                            .Sum(r => r.Bytes);


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
