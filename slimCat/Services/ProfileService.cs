﻿#region Copyright

// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ProfileService.cs">
//    Copyright (c) 2013, Justin Kadrovach, All rights reserved.
//   
//    This source is subject to the Simplified BSD License.
//    Please see the License.txt file for more information.
//    All other rights reserved.
//    
//    THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY 
//    KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
//    IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
//    PARTICULAR PURPOSE.
// </copyright>
//  --------------------------------------------------------------------------------------------------------------------

#endregion

namespace slimCat.Services
{
    #region Usings

    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Web;
    using System.Windows.Media;
    using HtmlAgilityPack;
    using Microsoft.Practices.Prism.Events;
    using Microsoft.Practices.Unity;
    using Models;
    using System.ComponentModel;
    using System.Diagnostics;
    using Models.Api;
    using Newtonsoft.Json;
    using Utilities;
    using HtmlDocument = HtmlAgilityPack.HtmlDocument;

    #endregion

    public class ProfileService : IProfileService
    {
        #region Fields
        private readonly IBrowser browser;

        private readonly IChatModel cm;

        private const string ProfileBodySelector = "//div[@id = 'tabs-1']/*[1]";

        private const string ProfileTagsSelector = "//div[@class = 'itgroup']";

        private const string ProfileKinksSeletor = "//td[contains(@class,'Character_Fetishlist')]";

        private const string ProfileIdSelector = "//input[@id = 'profile-character-id']";

        private readonly IUnityContainer container;

        private readonly IDictionary<string, ProfileData> profileCache = new Dictionary<string, ProfileData>(StringComparer.OrdinalIgnoreCase); 

        private readonly IDictionary<int, ProfileKink> kinkData = new Dictionary<int, ProfileKink>(); 

        #endregion

        #region Constructors
        public ProfileService(IUnityContainer contain, IBrowser browser, IChatModel cm, IEventAggregator events)
        {
            this.browser = browser;
            this.cm = cm;

            container = contain;

            events.GetEvent<CharacterSelectedLoginEvent>().Subscribe(GetProfileDataAsync);

            var worker = new BackgroundWorker();
            worker.DoWork += GetKinkDataAsync;
            worker.RunWorkerAsync();
        }
        #endregion

        #region Public Methods

        private void GetProfileDataAsyncHandler(object s, DoWorkEventArgs e)
        {
            var characterName = (string)e.Argument;
            PmChannelModel model = null;
            try
            {
                model = container.Resolve<PmChannelModel>(characterName);
            }
            catch (ResolutionFailedException)
            {
            }

            ProfileData cache;
            profileCache.TryGetValue(characterName, out cache);
            cache = cache ?? SettingsService.RetrieveProfile(characterName);
            if (cache != null)
            {
                if (!profileCache.ContainsKey(characterName))
                    cache.Kinks = cache.Kinks.Select(GetFullKink).ToList();

                if (cm.CurrentCharacter.NameEquals(characterName))
                    cm.CurrentCharacterData = cache;
                else if (model != null)
                    model.ProfileData = cache;

                profileCache[characterName] = cache;
                return;
            }

            var resp = browser.GetResponse(Constants.UrlConstants.CharacterPage + characterName, true);

            var htmlDoc = new HtmlDocument
            {
                OptionCheckSyntax = false
            };

            HtmlNode.ElementsFlags.Remove("option");
            htmlDoc.LoadHtml(resp);

            if (htmlDoc.DocumentNode == null)
                return;
            try
            {
                var profileBody = String.Empty;
                var profileText = htmlDoc.DocumentNode.SelectNodes(ProfileBodySelector);
                if (profileText != null)
                {
                    profileBody = WebUtility.HtmlDecode(profileText[0].InnerHtml);
                    profileBody = profileBody.Replace("<br>", "\n");
                }

                IEnumerable<ProfileTag> profileTags = new List<ProfileTag>();
                var fullSelection = htmlDoc.DocumentNode.SelectNodes(ProfileTagsSelector);
                if (fullSelection != null)
                {
                    profileTags = fullSelection.SelectMany(selection =>
                        selection.ChildNodes
                            .Where(x => x.Name == "span" || x.Name == "#text")
                            .Select(x => x.InnerText)
                            .ToList()
                            .Chunk(2)
                            .Select(x => x.ToList())
                            .Select(x => new ProfileTag
                            {
                                Label = DoubleDecode(x[0].Replace(":", "").Trim()),
                                Value = DoubleDecode(x[1].Trim())
                            }));
                }

                var allKinks = new List<ProfileKink>();
                var profileKinks = htmlDoc.DocumentNode.SelectNodes(ProfileKinksSeletor);
                if (profileKinks != null)
                {
                    allKinks = profileKinks.SelectMany(selection =>
                    {
                        var kind = (KinkListKind)Enum.Parse(typeof(KinkListKind), selection.Id.Substring("Character_Fetishlist".Length));
                        return selection.Descendants()
                            .Where(x => x.Name == "a")
                            .Select(x =>
                            {
                                var tagId = int.Parse(x.Id.Substring("Character_Listedfetish".Length));
                                var isCustomKink = x.Attributes.First(y => y.Name.Equals("class")).Value.Contains("FetishGroupCustom");
                                var tooltip = x.Attributes.FirstOrDefault(y => y.Name.Equals("rel"));
                                var name = x.InnerText.Trim();

                                return new ProfileKink
                                {
                                    Id = tagId,
                                    IsCustomKink = isCustomKink,
                                    Name = isCustomKink ? DoubleDecode(name) : string.Empty,
                                    KinkListKind = kind,
                                    Tooltip = tooltip != null && isCustomKink ? DoubleDecode(tooltip.Value) : string.Empty
                                };
                            });
                    }).ToList();
                }

                var id = htmlDoc.DocumentNode.SelectSingleNode(ProfileIdSelector).Attributes["value"].Value;

                var imageResp = browser.GetResponse(Constants.UrlConstants.ProfileImages,
                    new Dictionary<string, object> {{"character_id", id}}, true);
                var images = JsonConvert.DeserializeObject<ApiProfileImagesResponse>(imageResp);

                var profileData = CreateModel(profileBody, profileTags, images, allKinks);
                SettingsService.SaveProfile(characterName, profileData);

                profileCache[characterName] = profileData;

                if (model != null)
                    model.ProfileData = profileData;

                if (cm.CurrentCharacter.NameEquals(characterName))
                    cm.CurrentCharacterData = profileData;
            }
            catch {}
        }

        private string DoubleDecode(string s)
        {
            return WebUtility.HtmlDecode(WebUtility.HtmlDecode(s));
        }

        private void GetKinkDataAsync(object s, DoWorkEventArgs e)
        {
            var kinkDataCache = SettingsService.RetrieveProfile("!kinkdata");
            if (kinkDataCache == null)
            {
                var response = browser.GetResponse(Constants.UrlConstants.KinkList);

                var data = JsonConvert.DeserializeObject<ApiKinkDataResponse>(response);

                var apiKinks = data.Kinks
                    .SelectMany(x => x.Value.Kinks)
                    .Select(x => new ProfileKink
                {
                    Id = x.Id,
                    IsCustomKink = false,
                    Name = DoubleDecode(x.Name),
                    Tooltip = DoubleDecode(x.Description),
                    KinkListKind = KinkListKind.MasterList
                }).ToList();

                kinkDataCache = new ProfileData
                {
                    Kinks = apiKinks
                };
                SettingsService.SaveProfile("!kinkdata", kinkDataCache);
            }

            kinkData.Clear();
            kinkDataCache.Kinks.Each(x => kinkData.Add(x.Id, x));
        }

        [Conditional("DEBUG")]
        private static void Log(string text)
        {
            Logging.LogLine(text, "profile serv");
        }
        #endregion

        public void GetProfileDataAsync(string character)
        {
            var worker = new BackgroundWorker();
            worker.DoWork += GetProfileDataAsyncHandler;
            worker.RunWorkerAsync(character);
        }

        private ProfileKink GetFullKink(ProfileKink kink)
        {
            ProfileKink data;
            if (!kinkData.TryGetValue(kink.Id, out data)) return kink;

            kink.Name = data.Name;
            kink.Tooltip = data.Tooltip;
            return kink;
        } 

        private ProfileData CreateModel(string profileText, IEnumerable<ProfileTag> tags, ApiProfileImagesResponse imageResponse, IEnumerable<ProfileKink> kinks)
        {
            var allKinks = kinks.Select(GetFullKink);

            var toReturn = new ProfileData
            {
                ProfileText = profileText,
                Kinks = allKinks.ToList()
            };

            var tagActions = new Dictionary<string, Action<string>>(StringComparer.OrdinalIgnoreCase)
            {
                {"age", s => toReturn.Age = s},
                {"species", s => toReturn.Species = s},
                {"orientation", s => toReturn.Orientation = s},
                {"build", s => toReturn.Build = s},
                {"height/length", s => toReturn.Height = s},
                {"body type", s => toReturn.BodyType = s},
                {"position", s => toReturn.Position = s},
                {"dom/sub role", s => toReturn.DomSubRole = s}
            };

            var profileTags = tags.ToList();
            profileTags.Each(x =>
            {
                Action<string> action;
                if (tagActions.TryGetValue(x.Label, out action))
                    action(DoubleDecode(x.Value));
            });

            toReturn.AdditionalTags = profileTags
                .Where(x => !tagActions.ContainsKey(x.Label))
                .Select(x =>
            {
                x.Value = DoubleDecode(x.Value);
                return x;
            }).ToList();

            toReturn.Images = imageResponse.Images.Select(x => new ProfileImage(x)).ToList();

            toReturn.LastRetrieved = DateTime.Now;
            return toReturn;
        }
    }
}
