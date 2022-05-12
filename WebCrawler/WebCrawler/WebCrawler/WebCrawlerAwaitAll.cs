using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
            public bool Download { get; set; }
        }

        private string _filename;
        private long _totalSites = 0;
        private long _totalUniqueSites = 0;
        private long _totalResonsiveSites = 0;
        private long _totalBytesDownloaded = 0;

        private long _totalUnfoundHosts = 0;

        private long _totalDownloads = 0;

        private readonly object _mutexUrls = new object();
        private readonly object _mutexIps = new object();

        private HashSet<string> _uniqueUrls = new HashSet<string>();
        private HashSet<IPAddress> _uniqueIps = new HashSet<IPAddress>();

        private long _maxBytesFromPage = 5000;

        public WebCrawlerAwaitAll(string filename)
        {
            _filename = filename;
        }

        private async Task<SingleUrlResult> CheckUrlAsync(string url)
        {
            HttpClient httpClient = new HttpClient();

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
                        Bytes = 0,
                        Download = false
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
                        Bytes = 0,
                        Download = false
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
                    HttpRequestMessage headReq = new HttpRequestMessage();
                    headReq.Method = HttpMethod.Head;
                    headReq.RequestUri = uri;

                    HttpResponseMessage checkReply = await httpClient.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead);

                    if (!checkReply.IsSuccessStatusCode)
                    {
                        return new SingleUrlResult
                        {
                            Valid = true,
                            Unique = true,
                            Response = false,
                            Unfound = false,
                            Bytes = 0,
                            Download = false
                        };
                    }

                    if (checkReply.Content.Headers.ContentLength > _maxBytesFromPage)
                    {
                        //Interlocked.Increment(ref _unsuccessful);
                        return new SingleUrlResult
                        {
                            Valid = true,
                            Unique = true,
                            Response = false,
                            Unfound = false,
                            Bytes = 0,
                            Download = false
                        }; ;
                    }

                    byte[] result = await httpClient.GetByteArrayAsync(url);

                    return new SingleUrlResult
                    {
                        Valid = true,
                        Unique = true,
                        Response = true,
                        Unfound = false,
                        Bytes = result.LongLength,
                        Download = true
                    };
                }

                return new SingleUrlResult
                {
                    Valid = true,
                    Unique = false,
                    Response = false,
                    Unfound = false,
                    Bytes = 0,
                    Download = false
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
                        Bytes = 0,
                        Download = false
                    };
                }
                return new SingleUrlResult
                {
                    Valid = true,
                    Unique = true,
                    Response = false,
                    Unfound = false,
                    Bytes = 0,
                    Download = false
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
                    Bytes = 0,
                    Download = false
                };
            }
        }
        public async Task<WebCrawlResult> CrawlAsync(int? maxSites = null)
        {
            List<Task<SingleUrlResult>> tasks = new List<Task<SingleUrlResult>>();

            Stopwatch stopwatch = Stopwatch.StartNew();

            if (File.Exists(_filename))
            {
                using (StreamReader openFile = File.OpenText(_filename))
                {
                    string line = string.Empty;
                    int totalLines = 0;
                    while ((line = openFile.ReadLine()) != null)
                    {
                        totalLines++;
                        tasks.Add(CheckUrlAsync(line));
                        if (maxSites != null && totalLines >= maxSites)
                        {
                            Console.WriteLine($"Reader: reached max lines to read of {maxSites}");
                            break;
                        }
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

                    _totalDownloads = results.AsParallel()
                                            .Where(r => r.Download)
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
