﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AlarmBot.Models;
using AlarmBot.Topics;
using AlarmBot.TopicViews;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.BotFramework;
using Microsoft.Bot.Builder.Middleware;
using Microsoft.Bot.Builder.Storage;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;

namespace AlarmBot.Controllers
{
    [Route("api/[controller]")]
    public class MessagesController : Controller
    {
        public static BotFrameworkAdapter adapter = null;

        ///
        public MessagesController(IConfiguration configuration)
        {
            if (adapter == null)
            {
                string applicationId = configuration.GetSection(MicrosoftAppCredentials.MicrosoftAppIdKey)?.Value;
                string applicationPassword = configuration.GetSection(MicrosoftAppCredentials.MicrosoftAppPasswordKey)?.Value;

                // create the activity adapter that I will use to send/receive Activity objects with the user

                // pick your flavor of Key/Value storage
                IStorage storage = new FileStorage(System.IO.Path.GetTempPath());
                //IStorage storage = new MemoryStorage();
                //IStorage storage = new AzureTableStorage((System.Diagnostics.Debugger.IsAttached) ? "UseDevelopmentStorage=true;" : configuration.GetSection("DataConnectionString")?.Value, tableName: "AlarmBot");

                // create bot hooked up to the activity adapater
                adapter = new BotFrameworkAdapter(applicationId, applicationPassword)
                    .Use(new BotStateManager(storage)) // --- add Bot State Manager to automatically persist and load the context.State.Conversation and context.State.User objects
                    .Use(new RegExpRecognizerMiddleware()
                        .AddIntent("showAlarms", new Regex("show alarms(.*)", RegexOptions.IgnoreCase))
                        .AddIntent("addAlarm", new Regex("add alarm(.*)", RegexOptions.IgnoreCase))
                        .AddIntent("deleteAlarm", new Regex("delete alarm(.*)", RegexOptions.IgnoreCase))
                        .AddIntent("help", new Regex("help(.*)", RegexOptions.IgnoreCase))
                        .AddIntent("cancel", new Regex("cancel(.*)", RegexOptions.IgnoreCase))
                        .AddIntent("confirmYes", new Regex("(yes|yep|yessir|^y$)", RegexOptions.IgnoreCase))
                        .AddIntent("confirmNo", new Regex("(no|nope|^n$)", RegexOptions.IgnoreCase)));
            }
        }

        /// <summary>
        /// This simply handles calling the current conversation ITopic
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        private async Task TopicDispatcher(IBotContext context)
        {
            // --- Bot logic 
            bool handled = false;

            // Get the current ActiveTopic from my persisted conversation state
            var activeTopic = context.State.Conversation[ConversationProperties.ACTIVETOPIC] as ITopic;

            // if we don't have an active topic yet
            if (activeTopic == null)
            {
                // use the default topic
                activeTopic = new DefaultTopic();
                context.State.Conversation[ConversationProperties.ACTIVETOPIC] = activeTopic;
                handled = await activeTopic.StartTopic(context);
            }
            else
            {
                // we do have an active topic, so call it 
                handled = await activeTopic.ContinueTopic(context);
            }

            // if activeTopic's result is false and the activeTopic is NOT already the default topic
            if (handled == false && !(activeTopic is DefaultTopic))
            {
                // USe DefaultTopic as the active topic
                context.State.Conversation[ConversationProperties.ACTIVETOPIC] = new DefaultTopic();
                handled = await activeTopic.ResumeTopic(context);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody]Activity activity)
        {
            try
            {
                await adapter.ProcessActivty(this.Request.Headers["Authorization"].FirstOrDefault(), activity, this.TopicDispatcher);
                return this.Ok();
            }
            catch (UnauthorizedAccessException)
            {
                return this.Unauthorized();
            }
        }
    }
}
