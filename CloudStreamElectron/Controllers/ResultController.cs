﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ElectronNET.API;
using ElectronNET.API.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using CloudStreamElectron.Models;
using static CloudStreamElectron.Controllers.StaticData;
using Newtonsoft.Json;
using System.Reflection.Metadata;
using System.Net;
using static CloudStreamForms.Core.CloudStreamCore;
using CloudStreamForms.Core;
using System.IO;

namespace CloudStreamElectron.Controllers
{
	public class ResultController : Controller
	{
		private readonly ILogger<ResultController> _logger;

		public ResultController(ILogger<ResultController> logger)
		{
			_logger = logger;
		}

		public const string baseM3u8Name = @"mirrorlist.m3u8";
		public const string baseSubtitleName = @"mirrorlist.srt";
		public static string ConvertPathAndNameToM3U8(List<string> path, List<string> name, bool isSubtitleEnabled = false, string beforePath = "", string overrideSubtitles = null)
		{
			string _s = "#EXTM3U";
			if (isSubtitleEnabled) {
				_s += "\n";
				_s += "\n";
				//  _s += "#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=\"subs\",NAME=\"English\",DEFAULT=YES,AUTOSELECT=YES,FORCED=NO,LANGUAGE=\"en\",CHARACTERISTICS=\"public.accessibility.transcribes-spoken-dialog, public.accessibility.describes-music-and-sound\",URI=" + beforePath + baseSubtitleName + "\"";
				_s += "#EXTVLCOPT:sub-file=" + (overrideSubtitles ?? (beforePath + baseSubtitleName));
				_s += "\n";
			}
			for (int i = 0; i < path.Count; i++) {
				_s += "\n#EXTINF:" + ", " + name[i].Replace("-", "").Replace("  ", " ") + "\n" + path[i]; //+ (isSubtitleEnabled ? ",SUBTITLES=\"subs\"" : "");
			}
			return _s;
		}

		[Route("LoadPlayer")]
		[HttpGet]
		public async Task<string> LoadPlayer(int episode, string player, string guid)
		{
			try {
				error("GOT MESSAGE:::::");
				await Task.Delay(100);
				if (!CloudStreamElectron.Startup.isElectron) return "is not electron";
				var core = CoreHolder.GetCore(guid);
				if (core == null) return "invalid core";
				int normalEpisode = episode == -1 ? 0 : episode - 1;
				bool isMovie = core.activeMovie.title.IsMovie;
				var cEpisode = core.activeMovie.episodes[normalEpisode];
				string id = isMovie ? core.activeMovie.title.id : cEpisode.id;

				var _link = CloudStreamCore.GetCachedLink(id).Copy();
				if (_link == null) return "no links";

				Process p = new Process();
				p.StartInfo.FileName = player;
				p.StartInfo.UseShellExecute = true;

				//				int seekTo = 0;

				if (player == "vlc") {
					//--start-time 51
					p.StartInfo.Arguments = "--fullscreen vlc://quit";
				}
				else if (player == "mpv") {
					//--start=+56
					//playlist-pos
					//playlist-current-pos
					p.StartInfo.Arguments = $"--title={(isMovie ? core.activeMovie.title.name : cEpisode.name)}";
				}
				var endPath = Path.Combine(Directory.GetCurrentDirectory(), baseM3u8Name);
				if (System.IO.File.Exists(endPath)) {
					System.IO.File.Delete(endPath);
				}
				var links = _link.Value.links.Where(t => !t.isAdvancedLink);
				System.IO.File.WriteAllText(endPath, ConvertPathAndNameToM3U8(links.Select(t => t.baseUrl).ToList(), links.Select(t => t.PublicName).ToList()));

				p.StartInfo.FileName = endPath;
				p.Start();

				return "true";
			}
			catch (Exception _ex) {
				error(_ex);
				return _ex.Message;
			}
		}


		[Route("GetLoadLinks")]
		[HttpGet]   //use or not works same
					//[ValidateAntiForgeryToken]
		public async Task<string> GetLoadLinks(int episode, int season, int delay, string guid, bool isDub, bool reloadAllLinks)
		{
			try {
				error("TRYLOAD");
				var core = CoreHolder.GetCore(guid);
				if (core == null) return "";
				const int maxDelay = 60000;
				if (delay > maxDelay) delay = maxDelay;

				int normalEpisode = episode == -1 ? 0 : episode - 1;
				bool isMovie = core.activeMovie.title.IsMovie;

				string id = isMovie ? core.activeMovie.title.id : core.activeMovie.episodes[normalEpisode].id;
				if (delay > 0) {
					if (reloadAllLinks) {
						CloudStreamCore.ClearCachedLink(id);
					}
					core.GetEpisodeLink(episode, season, isDub: isDub);
					await Task.Delay(delay);
				}

				if (core.activeMovie.episodes == null && !isMovie) return "";
				var _link = CloudStreamCore.GetCachedLink(id).Copy();
				if (_link.HasValue) {
					var json = JsonConvert.SerializeObject(_link);
					return (json);
				}
				else {
					return "no links";
				}
			}
			catch (Exception _ex) {
				return "";
			}
		}

		[System.Serializable]
		struct ResultSeasonData
		{
			public List<Episode> Episodes;
			public bool subExists;
			public bool dubExists;
		}

		[Route("GetSeasons")]
		[HttpGet]   //use or not works same
					//[ValidateAntiForgeryToken]
		public async Task<string> GetSeasons(int season, string guid, bool isDub)
		{
			var core = CoreHolder.GetCore(guid);
			if (core == null) return "";

			static string GetPoster(string _poster)
			{
				return CloudStreamCore.ConvertIMDbImagesToHD(_poster, 2240, 1260, 1, false, true);
			}


			if (core.activeMovie.title.IsMovie) {
				core.GetImdbEpisodes(season);

				string poster = "";
				try {
					poster = core.activeMovie.title.trailers[0].PosterUrl;
				}
				catch (Exception) {
				}
				return JsonConvert.SerializeObject(new ResultSeasonData() {
					Episodes = new List<Episode>() {
							new Episode() {
								name = core.activeMovie.title.name,
								description = core.activeMovie.title.description,
								id = core.activeMovie.title.id,
								posterUrl =GetPoster(poster),
								rating = core.activeMovie.title.rating,
							}
						},
					dubExists = false,
					subExists = false
				});
			}


			List<Episode> episodes = null;
			bool done = false;
			void Core_EpisodesLoaded(object sender, List<Episode> e)
			{
				done = true;
				episodes = e;
				core.episodeLoaded -= Core_EpisodesLoaded;
			}
			core.episodeLoaded += Core_EpisodesLoaded;
			core.GetImdbEpisodes(season);
			while (!done) {
				await Task.Delay(50);
			}

			episodes = episodes.Select(t => {
				t.posterUrl = GetPoster(t.posterUrl);
				return t;
			}).ToList();

			bool subExists = false;
			bool dubExists = false;

			int maxEpisodes = episodes.Count;

			if (core.activeMovie.title.movieType == MovieType.Anime) {
				core.GetSubDub(season, out subExists, out dubExists);
				if (!dubExists && isDub) {
					isDub = false;
				}
				maxEpisodes = core.GetMaxEpisodesInAnimeSeason(season, isDub);
			}

			var json = JsonConvert.SerializeObject(new ResultSeasonData() { Episodes = episodes.GetRange(0, maxEpisodes), dubExists = dubExists, subExists = subExists });
			return (json);
		}

		readonly List<string> genres = new List<string>() { "", "action", "adventure", "animation", "biography", "comedy", "crime", "drama", "family", "fantasy", "film-noir", "history", "horror", "music", "musical", "mystery", "romance", "sci-fi", "sport", "thriller", "war", "western" };
		readonly List<string> genresNames = new List<string>() { "Any", "Action", "Adventure", "Animation", "Biography", "Comedy", "Crime", "Drama", "Family", "Fantasy", "Film-Noir", "History", "Horror", "Music", "Musical", "Mystery", "Romance", "Sci-Fi", "Sport", "Thriller", "War", "Western" };

		static readonly Dictionary<int, string> cashedTop = new Dictionary<int, string>();

		[Route("FetchTop100")]
		[HttpGet]
		public async Task<string> FetchTop100(int start, bool isTop100, bool isAnime, string guid)
		{
			var core = CoreHolder.GetCore(guid);

			int casheId = (isTop100 ? 1 : 0) + (isAnime ? 20 : 10) + (start * 100);
			if(cashedTop.ContainsKey(casheId)) {
				return cashedTop[casheId];
			}
			else {
				var list = await core.FetchTop100(new List<string>() { genres[0] }, start, top100: isTop100, isAnime: isAnime, upscale: true, multi: 5);
				string _data = JsonConvert.SerializeObject(list);
				cashedTop[casheId] = _data;
				return _data;
			}
		}


		[Route("LoadTitle")]
		[HttpGet]   //use or not works same
					//[ValidateAntiForgeryToken]
		public async Task<string> LoadTitle(string _url, string guid)
		{
			_url = RemoveHtmlChars(_url);
			string url = "";
			string year = "";
			string name = "";
			_url += "&";
			url = FindHTML(_url, "url=", "&");
			year = FindHTML(_url, "year=", "&");
			name = FindHTML(_url, "name=", "&", decodeToNonHtml: true).Replace("%20", " ");

			var _g = CoreHolder.CheckGuid(guid);

			if (url != "") {
				var core = CoreHolder.GetCore(_g);
				if (core == null) return "";
				bool done = false;
				Movie m = new Movie();
				void Core_titleLoaded(object sender, Movie e)
				{
					m = e;
					// e.title.name
					done = true;
					core.titleLoaded -= Core_titleLoaded;
				}
				core.titleLoaded += Core_titleLoaded;
				core.GetImdbTitle(new Poster() { url = url, name = name, year = year, });

				while (!done) {
					await Task.Delay(50);
				}
				var mClone = (Movie)m.Copy();
				mClone.title.hdPosterUrl = CloudStreamCore.ConvertIMDbImagesToHD(mClone.title.hdPosterUrl, null, null, 2);
				var json = JsonConvert.SerializeObject(mClone);
				json = json[0..^1];
				json += $",\"Guid\":\"{_g.ToString()}\"}}";
				return (json);
			}
			return _url;
			//return (JsonConvert.SerializeObject(core.activeSearchResults.ToArray()));
		}

		public IActionResult Index()
		{
			// var q = ControllerContext.HttpContext.Request.Query; 
			return View();
		}

		[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
		public IActionResult Error()
		{
			return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
		}
	}

}