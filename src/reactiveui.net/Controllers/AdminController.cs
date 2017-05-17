﻿// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using reactiveui.net.Models;
using reactiveui.net.Services;
using reactiveui.net.ViewModels;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace reactiveui.net.Controllers
{
    [Route("/admin")]
    [Authorize("Admin")]
    public class AdminController : Controller
    {
        private readonly ILiveShowDetailsService _liveShowDetails;
        private readonly IMemoryCache _memoryCache;
        private readonly AppSettings _appSettings;
        private readonly IHostingEnvironment _env;
        private readonly TelemetryClient _telemetry;

        public AdminController(
            IHostingEnvironment env,
            ILiveShowDetailsService liveShowDetails,
            IMemoryCache memoryCache,
            IOptions<AppSettings> appSettings,
            TelemetryClient telemetry)
        {
            _liveShowDetails = liveShowDetails;
            _memoryCache = memoryCache;
            _appSettings = appSettings.Value;
            _env = env;
            _telemetry = telemetry;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = new AdminViewModel
            {
                SuccessMessage = (string)TempData[nameof(AdminViewModel.SuccessMessage)]
            };

            var liveShowDetails = await _liveShowDetails.LoadAsync();

            UpdateAdminViewModel(model, liveShowDetails);

            return View(model);
        }

        [ModelMetadataType(typeof(AdminViewModel))]
        public class AdminInputModel
        {
            public string LiveShowEmbedUrl { get; set; }

            public string LiveShowHtml { get; set; }

            public DateTime? NextShowDateAest { get; set; }

            public string AdminMessage { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> Save(AdminInputModel input)
        {
            var liveShowDetails = await _liveShowDetails.LoadAsync() ?? new LiveShowDetails();

            if (!ModelState.IsValid)
            {
                // Model validation error, just return and let the error render
                var viewModel = new AdminViewModel();
                UpdateAdminViewModel(viewModel, liveShowDetails);

                return View(nameof(Index), viewModel);
            }

            if (!string.IsNullOrEmpty(input.LiveShowEmbedUrl) && input.LiveShowEmbedUrl.StartsWith("http://"))
            {
                input.LiveShowEmbedUrl = "https://" + input.LiveShowEmbedUrl.Substring("http://".Length);
            }

            TrackShowEvent(input, liveShowDetails);

            liveShowDetails.LiveShowEmbedUrl = input.LiveShowEmbedUrl;
            liveShowDetails.LiveShowHtml = input.LiveShowHtml;
            liveShowDetails.NextShowDateUtc = input.NextShowDateAest?.ConvertFromPtcToUtc();
            liveShowDetails.AdminMessage = input.AdminMessage;

            await _liveShowDetails.SaveAsync(liveShowDetails);

            TempData[nameof(AdminViewModel.SuccessMessage)] = "Live show details saved successfully!";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost("clearcache")]
        public IActionResult ClearCache()
        {
            _memoryCache.Remove(YouTubeShowsService.CacheKey);

            TempData[nameof(AdminViewModel.SuccessMessage)] = "YouTube cache cleared successfully!";

            return RedirectToAction(nameof(Index));
        }

        private void TrackShowEvent(AdminInputModel input, LiveShowDetails liveShowDetails)
        {
            if (_telemetry.IsEnabled())
            {
                var showStarted = string.IsNullOrEmpty(liveShowDetails.LiveShowEmbedUrl) && !string.IsNullOrEmpty(input.LiveShowEmbedUrl);
                var showEnded = !string.IsNullOrEmpty(liveShowDetails.LiveShowEmbedUrl) && string.IsNullOrEmpty(input.LiveShowEmbedUrl);

                if (showStarted || showEnded)
                {
                    var showEvent = new EventTelemetry(showStarted ? "Show Started" : "Show Ended");
                    showEvent.Properties.Add("Show Embed URL", showStarted ? input.LiveShowEmbedUrl : liveShowDetails.LiveShowEmbedUrl);
                    _telemetry.TrackEvent(showEvent);
                }
            }
        }

        private void UpdateAdminViewModel(AdminViewModel model, LiveShowDetails liveShowDetails)
        {
            model.LiveShowEmbedUrl = liveShowDetails?.LiveShowEmbedUrl;
            model.LiveShowHtml = liveShowDetails?.LiveShowHtml;
            model.NextShowDateAest = liveShowDetails?.NextShowDateUtc?.ConvertFromUtcToAest();
            model.AdminMessage = liveShowDetails?.AdminMessage;

            var nextTuesday = GetNextTuesday();
            model.NextShowDateSuggestionAestAM = nextTuesday.AddHours(10).ToString("dd/MM/yyyy HH:mm");
            model.NextShowDateSuggestionAestPM = nextTuesday.AddHours(15).AddMinutes(45).ToString("dd/MM/yyyy HH:mm");

            model.AppSettings = _appSettings;
            model.EnvironmentName = _env.EnvironmentName;
        }

        private DateTime GetNextTuesday()
        {
            var nowPst = DateTime.UtcNow.ConvertFromUtcToAest();
            var remainingDays = 7 - ((int) nowPst.DayOfWeek + 5) % 7;
            var nextTuesday = nowPst.AddDays(remainingDays);

            return nextTuesday.Date;
        }
    }
}
