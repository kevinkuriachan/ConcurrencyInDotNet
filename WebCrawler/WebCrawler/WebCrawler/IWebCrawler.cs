﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebCrawler.WebCrawler
{
    public interface IWebCrawler
    {
        public Task<WebCrawlResult> CrawlAsync(int? maxSites = null);
    }
}
