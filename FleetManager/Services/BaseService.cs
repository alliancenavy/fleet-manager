﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp.Server;
using ANWI;
using NLog;
using WebSocketSharp;
using System.IO;
using MsgPack.Serialization;

namespace FleetManager.Services {

	/// <summary>
	/// Provides common functionality for all services
	/// </summary>
	public abstract class BaseService : WebSocketBehavior {

		protected NLog.Logger logger = null;

		private Dictionary<string, ConnectedUser> connectedUsers = null;

		private Dictionary<Type, Func<ANWI.Messaging.IMessagePayload,
			ANWI.Messaging.IMessagePayload>> msgProcessors
			= new Dictionary<Type, Func<ANWI.Messaging.IMessagePayload, 
				ANWI.Messaging.IMessagePayload>>();

		protected abstract string GetTokenCookie();
		protected abstract string GetNameCookie();
		protected abstract string GetLogIdentifier();

		protected BaseService(string name, bool trackUsers) {
			logger = LogManager.GetLogger(name);

			if (trackUsers)
				connectedUsers = new Dictionary<string, ConnectedUser>();
		}

		/// <summary>
		/// Adds a message processor function which will be automatically
		/// called when the appropriate message comes in.
		/// </summary>
		/// <param name="t"></param>
		/// <param name="processor"></param>
		protected void
		AddProcessor(Type t, Func<ANWI.Messaging.IMessagePayload, 
			ANWI.Messaging.IMessagePayload> processor) {
			msgProcessors.Add(t, processor);
		}

		/// <summary>
		/// Returns the connected user for a given token
		/// </summary>
		/// <param name="token"></param>
		/// <returns></returns>
		protected ConnectedUser GetUser(string token) {
			return connectedUsers[token];
		}

		/// <summary>
		/// New connection starting point
		/// </summary>
		protected override void OnOpen() {
			base.OnOpen();
			logger.Info($"Connection received from {GetLogIdentifier()}");

			if (connectedUsers != null) {
				// Add this user to the connected list
				connectedUsers.Add(
					GetTokenCookie(),
					new ConnectedUser(Context)
					);

				logger.Info("Connected user count now " + connectedUsers.Count);
			}
		}

		/// <summary>
		/// Connection ending point
		/// </summary>
		/// <param name="e"></param>
		protected override void OnClose(CloseEventArgs e) {
			base.OnClose(e);
			logger.Info($"Connection from {GetLogIdentifier()} closed");

			if (connectedUsers != null) {
				// Remove user from the dictionary
				connectedUsers.Remove(GetTokenCookie());

				logger.Info("Conneccted user count now " + connectedUsers.Count);
			}
		}

		/// <summary>
		/// Socket error handler
		/// </summary>
		/// <param name="e"></param>
		protected override void OnError(WebSocketSharp.ErrorEventArgs e) {
			base.OnError(e);
		}

		/// <summary>
		/// Message router.  Sends messages to correct processor function
		/// </summary>
		/// <param name="e"></param>
		protected sealed override void OnMessage(MessageEventArgs e) {
			ANWI.Messaging.Message msg
				= ANWI.Messaging.Message.Receive(e.RawData);

			logger.Info($"Message received #{msg.sequence} from " +
				$"{GetLogIdentifier()}. {msg.payload.ToString()}");

			Func<ANWI.Messaging.IMessagePayload, ANWI.Messaging.IMessagePayload>
				processor = msgProcessors[msg.payload.GetType()];
			if(processor != null) {
				ANWI.Messaging.IMessagePayload p = processor(msg.payload);

				if (p != null) {
					ANWI.Messaging.Message response
						= new ANWI.Messaging.Message(msg.sequence, p);
					Send(response);
				}

			} else {
				logger.Error("No message processor found for payload type " +
					msg.payload.GetType());
			}
		}

		/// <summary>
		/// Serializes an ANWI message and sends it to the current context
		/// </summary>
		/// <param name="msg"></param>
		protected void Send(ANWI.Messaging.Message msg) {
			MemoryStream stream = new MemoryStream();
			MessagePackSerializer.Get<ANWI.Messaging.Message>().Pack(
				stream, msg);
			Send(stream.ToArray());
		}
	}
}
