using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace WebCrawler.WebCrawler
{
    public class WebCrawlerSingleThreaded : IWebCrawler
    {
        private string _filename;
        private long _totalSites = 0;
        private long _totalUniqueSites = 0;
        private long _totalResonsiveSites = 0;
        private long _totalBytesDownloaded = 0;

        private long _totalUnfoundHosts = 0;

        private HashSet<string> _uniqueUrls = new HashSet<string>();
        private HashSet<IPAddress> _uniqueIps = new HashSet<IPAddress>();

        public WebCrawlerSingleThreaded(string filename)
        {
            _filename = filename;
        }

        public async Task<WebCrawlResult> CrawlAsync()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            if (File.Exists(_filename))
            {
                string[] lines = File.ReadAllLines(_filename);

                WebClient client = new WebClient();

                foreach (string line in lines)
                {
                    _totalSites++;
                    if (_uniqueUrls.Contains(line))
                    {
                        continue;
                    }

                    try
                    {
                        //Console.WriteLine($"{_totalSites} - Checking {line}");
                        Uri uri = new Uri(line);
                        IPHostEntry dnsResult = await Dns.GetHostEntryAsync(uri.Authority);

                        long prevUniqueIps = _uniqueIps.Count;

                        if (dnsResult.AddressList.Length > 0)
                        {
                            foreach (IPAddress ip in dnsResult.AddressList)
                            {
                                _ = _uniqueIps.Add(ip);
                            }
                        }

                        // a new unique ip was found 
                        if (_uniqueIps.Count != prevUniqueIps)
                        {
                            _totalUniqueSites++;
                            byte[] result = await client.DownloadDataTaskAsync(new Uri(line));
                            _totalResonsiveSites++;
                            _totalBytesDownloaded += result.LongLength;
                        }
                    }
                    catch (SocketException ex)
                    {
                        if (ex.ErrorCode == (int)SocketError.HostNotFound)
                        {
                            _totalUnfoundHosts++;
                        }
                        continue;
                    }
                    catch (Exception ex)
                    {
                        continue;
                    }
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
