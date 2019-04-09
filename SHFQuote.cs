using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace shf.quote
{
    public static class SHFQuote
    {
        static readonly string SlackApiToken = Environment.GetEnvironmentVariable("SlackApiToken");

        static readonly string SlackWebhookUrl = Environment.GetEnvironmentVariable("SlackWebhookUrl");

        static readonly string SlackApiChannelsUrl = "https://slack.com/api/conversations.list";

        static readonly string SlackApiPinsUrl = "https://slack.com/api/pins.list";

        static readonly string SlackApiUsersUrl = "https://slack.com/api/users.list";
        
        static readonly IList<String> ExcludedChannels =  new List<String> { "thracia", "playground", "programming", "gm-talk", "civ", "hotsow" };

        static HttpClient m_Client = new HttpClient();


        [FunctionName("SHFQuote")]
        public static async void Run([TimerTrigger("0 0 7 * * Mon")]TimerInfo myTimer, ILogger log)
        {
            string message = "";

            var channel = await GetRandomChannel();

            if (channel == null) 
            {
                log.LogWarning("Ending execution because no channel was found.");
                return;
            }

            var pin = await GetRandomPinnedMessageForChannel(channel.Id);

            if (pin == null)
            {
                log.LogWarning("Ending execution because no pinned message was found.");
                return;
            }

            var user = await GetUserNameForUserId(pin.Author);

            if (String.IsNullOrEmpty(user))
            {
                user = "A wise soul";
            }

            message = "It's Monday! Here is the *Super Hobby Friends Quote of the Week*: ";
            message += "\"" +pin.Text +"\" - " +user +", <!date^" +ReformatMessageTimestamp(pin.TimeStamp) +"^{date}|some nebulous point in the past>";

            var result = await m_Client.PostAsJsonAsync(SlackWebhookUrl, new { text = message });
        }

        private static IDictionary<string, string> BuildQueryParamDictionary(string[] queryNamesP, string[] queryValuesP) 
        {
            if (queryNamesP.Length != queryValuesP.Length)
            {
                throw new Exception("The amount of query parameter names does not match the amount of values.");
            }

            IDictionary<string, string> query_params = new Dictionary<string, string>();

            for (int i = 0; i < queryNamesP.Length; i++)
            {
                query_params.Add(queryNamesP[i], queryValuesP[i]);
            }

            return query_params;
        }

        private static async Task<T> DoApiRequest<T>(string urlP) 
        {
            var response =  await m_Client.GetAsync(urlP);
            var result_json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(result_json);
        }

        private static async Task<IList<SlackChannel>> GetChannelListFromSlack() 
        {
            var result = await DoApiRequest<SlackConversationsResult>(QueryHelpers.AddQueryString(SlackApiChannelsUrl, GetChannelsListQueryParams()));
            return result.Channels;
        }

        private static IDictionary<string, string> GetChannelsListQueryParams() 
        {
            return BuildQueryParamDictionary(new string[] { "token", "exclude_archived", "types" }, 
                                             new string[] { SlackApiToken, "true", "public_channel,private_channel"});
        }

        private static async Task<IList<SlackPin>> GetPinsForChannelFromSlack(string channelIdP) 
        {
            var result = await DoApiRequest<SlackPinsResult>(QueryHelpers.AddQueryString(SlackApiPinsUrl, GetPinnedMessageQueryParams(channelIdP)));
            return result.Pins;
        }

        private static IDictionary<string, string> GetPinnedMessageQueryParams(string channelIdP) 
        {
            return BuildQueryParamDictionary(new string[] { "token", "channel" }, 
                                             new string[] { SlackApiToken, channelIdP });

        }

        private static T GetRandom<T>(IList<T> listP) {

            if (listP.Count <= 0) {
                return default(T);
            }

            Random random = new Random();
            return listP[random.Next(0, listP.Count - 1)];
        }

        private static async Task<SlackChannel> GetRandomChannel() 
        {
            IList<SlackChannel> channels = await GetChannelListFromSlack();

            SlackChannel channel = GetRandom<SlackChannel>(channels.Where(channelP => !ExcludedChannels.Contains(channelP.Name)).ToList());

            return channel;
        }

        private static async Task<SlackMessage> GetRandomPinnedMessageForChannel(string channelIdP) 
        {
            IList<SlackPin> pins = await GetPinsForChannelFromSlack(channelIdP);

            SlackPin pin = GetRandom<SlackPin>(pins);

            return pin.Message;
        }

        private static async Task<IList<SlackUser>> GetUserListFromSlack()
        {
            var result = await DoApiRequest<SlackUsersResult>(QueryHelpers.AddQueryString(SlackApiUsersUrl, GetUserListQueryParams()));
            return result.Users;
        }

        private static IDictionary<string, string> GetUserListQueryParams()
        {
            return BuildQueryParamDictionary(new string[] { "token" },
                                             new string[] { SlackApiToken });
        }

        private static async Task<string> GetUserNameForUserId(string userIdP)
        {
            IList<SlackUser> users = await GetUserListFromSlack();

            SlackUser user = users.FirstOrDefault(userP => userP.Id.Equals(userIdP));

            return user != null ? user.Name : "A wise soul";
        }

        private static string ReformatMessageTimestamp(string messageTimestampP) 
        {
            int decimal_index = messageTimestampP.IndexOf('.');

            if (decimal_index > 0)
            {
                return messageTimestampP.Substring(0, decimal_index);
            }

            return messageTimestampP;
        }

        private class SlackPayload
        {
        [JsonProperty("channel")]
        public string Channel { get; set; }
            
        [JsonProperty("username")]
        public string Username { get; set; }
            
        [JsonProperty("text")]
        public string Text { get; set; }
        }

        private class SlackConversationsResult 
        {
            [JsonProperty("ok")]
            public bool ok { get; set; }

            [JsonProperty("channels")]
            public IList<SlackChannel> Channels { get; set; }
        }

        private class SlackChannel
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }
        }

        private class SlackPinsResult
        {
            [JsonProperty("items")]
            public IList<SlackPin> Pins { get; set; }
        }

        private class SlackPin
        {
            [JsonProperty("message")]
            public SlackMessage Message { get; set; }
        }

        private class SlackMessage
        {
            [JsonProperty("permalink")]
            public string Permalink { get; set; }

            [JsonProperty("text")]
            public string Text { get; set; }

            [JsonProperty("ts")]
            public string TimeStamp { get; set; }

            [JsonProperty("user")]
            public string Author { get; set; }
        }

        private class SlackUsersResult
        {
            [JsonProperty("ok")]
            public bool ok { get; set; }

            [JsonProperty("members")]
            public IList<SlackUser> Users { get; set; }
        }

        private class SlackUser
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }
        }
    }
}
