﻿/*
 * Magic, Copyright(c) Thomas Hansen 2019 - 2020, thomas@servergardens.com, all rights reserved.
 * See the enclosed LICENSE file for details.
 */

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MailKit.Net.Pop3;
using magic.node;
using magic.node.extensions;
using magic.signals.contracts;
using magic.lambda.mail.helpers;

namespace magic.lambda.mail
{
    /// <summary>
    /// Fetches all new messages from the specified POP3 account.
    /// </summary>
    [Slot(Name = "wait.mail.pop3.fetch")]
    public class MailPop3Fetch : ISlotAsync
    {
        readonly IConfiguration _configuration;

        public MailPop3Fetch(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Implementation of your slot.
        /// </summary>
        /// <param name="signaler">Signaler that raised the signal.</param>
        /// <param name="input">Arguments to your slot.</param>
        public async Task SignalAsync(ISignaler signaler, Node input)
        {
            // Retrieving server connection settings.
            var settings = new ConnectionSettings(_configuration, input, "pop3");

            // Maximum number of emails to fetch.
            var max = input.Children.SingleOrDefault(x => x.Name == "max")?.GetEx<int>() ?? 50;

            // Retrieving lambda callback for what to do for each message.
            var lambda = input.Children.FirstOrDefault(x => x.Name == ".lambda") ??
                throw new ArgumentNullException("No [.lambda] provided to [wait.mail.pop3.fetch]");

            // Creating client, and fetching all new messages, invoking [.lambda] for each message fetched.
            using (var client = new Pop3Client())
            {
                // Connecting and authenticating (unless username is null)
                await client.ConnectAsync(settings.Server, settings.Port, settings.Secure);
                if (settings.Username != null)
                    await client.AuthenticateAsync(settings.Username, settings.Password);

                // Retrieving [max] number of emails.
                for (var idx = 0; idx < client.Count && (max == -1 || client.Count < max); idx++)
                {
                    // Getting message, and parsing to lambda
                    var message = await client.GetMessageAsync(idx);
                    var parseNode = new Node("", message);
                    signaler.Signal(".mime.parse", parseNode);

                    // Invoking [.lambda] with message as argument.
                    var exe = lambda.Clone();
                    var messageNode = new Node(".message");
                    messageNode.AddRange(parseNode.Children);
                    exe.Add(messageNode);
                    await signaler.SignalAsync("eval", exe);
                }
                await client.DisconnectAsync(true);
            }
        }
    }
}
