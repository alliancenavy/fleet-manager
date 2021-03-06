﻿using ANWI;
using ANWI.FleetComp;
using MsgPack.Serialization;
using System.Collections.Generic;
using System.IO;
using WebSocketSharp.Net.WebSockets;
using NLog;
using System;

namespace FleetManager {

	/// <summary>
	/// An active operation
	/// </summary>
	public class ActiveOperation {
		private Logger logger = null;

		#region Instance Members
		public string uuid { get; private set; }
		public long timestamp { get; private set; }
		public int FCID { get; private set; }

		private string name;
		private OperationType type;
		public OperationStatus status { get; private set; }
		private bool freeMove;
		private bool C2Unified;

		private List<OpParticipant> roster = new List<OpParticipant>();
		public int rosterCount { get { return roster.Count; } }

		private OrderOfBattle fleet = new OrderOfBattle();

		private Dictionary<string, ConnectedUser> subscribed
			= new Dictionary<string, ConnectedUser>();
		public int subscribedCount { get { return subscribed.Count; } }

		private int lastWingNumber = 1;
		#endregion

		#region Constructors
		public 
		ActiveOperation(string uuid, string name, OperationType type, int fc) {
			this.uuid = uuid;
			this.name = name;
			this.type = type;
			this.status = OperationStatus.CONFIGURING;
			this.freeMove = true;
			this.C2Unified = true;
			this.FCID = fc;
			this.timestamp = GetTimestamp();

			logger = LogManager.GetLogger($"Op {uuid}");
		}

		public LiteOperation ToLite() {
			return new LiteOperation() {
				uuid = uuid,
				name = name,
				type = type,
				status = status,
				currentMembers = rosterCount,
				neededMembers = fleet.TotalCriticalPositions,
				totalSlots = fleet.TotalPositions
			};
		}

		public Operation ToSnapshot() {
			return new Operation() {
				uuid = uuid,
				name = name,
				type = type,
				status = status,
				freeMove = freeMove,
				C2Unified = C2Unified,
				roster = roster,
				fleet = new List<FleetUnit>(fleet.Fleet)
			};
		}
		#endregion

		#region Helpers
		private OpParticipant GetParticipant(int id) {
			foreach(OpParticipant p in roster) {
				if (p.profile.id == id)
					return p;
			}

			logger.Error($"User {id} is not in the roster");
			return null;
		}

		private ConnectedUser GetSubscriber(string token) {
			ConnectedUser user;
			if (!subscribed.TryGetValue(token, out user)) {
				return null;
			}
			return user;
		}

		private void SendUpdateSettings() {
			PushToAll(new ANWI.Messaging.Ops.UpdateSettings() {
				freeMove = freeMove,
				C2Unified = C2Unified
			});
		}

		private long GetTimestamp() {
			return DateTime.UtcNow.Ticks;
		}
		#endregion

		#region Participant Management
		public void SubscribeUser(ConnectedUser user) {
			subscribed.Add(user.token, user);

			// Force this user to join if they are the FC
			if (user.profile.id == FCID) {
				JoinUser(user.token);

				// Add some fake users for testing purposes
				/*for(int i = 4; i < 35; ++i) {
					if (i == 11 || i == 12 || i == 14)
						continue;

					string token = ANWI.Utility.UUID.GenerateUUID();
					SubscribeUser(new ConnectedUser(token, i));
					JoinUser(token);
				}*/
			}
		}

		public void UnsubscribeUser(string token) {
			ConnectedUser user = GetSubscriber(token);
			if (user == null)
				return;

			// Remove them from the roster if they are joined
			RemoveUser(user.profile.id);

			subscribed.Remove(token);
		}

		public void AdvanceLifecycle() {
			status = status.Next();
			timestamp = GetTimestamp();
			PushToAll(new ANWI.Messaging.Ops.UpdateStatus(status));
		}

		public void JoinUser(string token) {
			ConnectedUser user = GetSubscriber(token);
			if(user == null) {
				logger.Error("Attempted to join user but they are not subbed");
				return;
			}

			OpParticipant np = new OpParticipant();
			np.profile = user.profile;
			np.position = null;
			np.isFC = user.profile.id == FCID;

			roster.Add(np);

			PushToAll(new ANWI.Messaging.Ops.UpdateRoster(
					new List<OpParticipant>() { np },
					null
				));
		}

		public void RemoveUser(int id) {
			OpParticipant user = GetParticipant(id);
			if (user == null)
				return;

			// If the user is currently in a position unassign it
			if(user.position != null) {
				string posUUID = user.position.uuid;
				fleet.ClearPosition(posUUID);
				PushToAll(new ANWI.Messaging.Ops.UpdateAssignments(
					null,
					null,
					new List<string>() { posUUID }
					));
			}

			// Remove them from the roster and send update
			roster.Remove(user);
			PushToAll(new ANWI.Messaging.Ops.UpdateRoster(
				null,
				new List<int>() { user.profile.id }
				));
		}

		public void SetFreeMove(bool fm) {
			freeMove = fm;

			SendUpdateSettings();
		}

		public void SetC2Type(bool unified) {
			C2Unified = unified;

			SendUpdateSettings();
		}
		#endregion

		#region Fleet Composition
		public void AddFleetShip(int id) {
			Ship ship = new Ship();
			ship.uuid = ANWI.Utility.UUID.GenerateUUID();
			ship.v = LiteVessel.FetchById(id);
			ship.isFlagship = false;

			fleet.AddUnit(ship);

			PushToAll(new ANWI.Messaging.Ops.UpdateUnitsShips(
					new List<Ship>() { ship },
					null
				));
		}

		public void AddWing() {
			Wing wing = new Wing() {
				uuid = ANWI.Utility.UUID.GenerateUUID(),
				name = $"New Wing {lastWingNumber}",
				callsign = "No Callsign",
				primaryRole = Wing.Role.CAP
			};

			fleet.AddUnit(wing);

			PushToAll(new ANWI.Messaging.Ops.UpdateUnitsWings(
				new List<Wing>() { wing },
				null));
		}

		public void AddBoat(string wingUUID, int hullID) {
			Boat boat = new Boat() {
				uuid = ANWI.Utility.UUID.GenerateUUID(),
				wingUUID = wingUUID,
				type = Hull.FetchById(hullID),
				isWC = false
			};

			// Add a pilot position by default to make things
			// easy for the FC
			boat.positions.Add(new OpPosition() {
				uuid = ANWI.Utility.UUID.GenerateUUID(),
				unitUUID = boat.uuid,
				critical = false,
				role = new OperationRole() {
					id = 4,
					name = "Pilot",
					rateAbbrev = "FP"
				}
			});

			fleet.AddUnit(boat);

			PushToAll(new ANWI.Messaging.Ops.UpdateUnitsBoats(
				new List<Boat>() { boat },
				null));
		}

		public void DeleteFleetUnit(string uuid) {

			// No need to push assingment changes to users because the OOB
			// class will do it automatically on their end.
			fleet.DeleteUnit(uuid);
			
			PushToAll(new ANWI.Messaging.Ops.UpdateUnitsShips(
				null,
				new List<string>() { uuid }));
		}

		public void ModifyUnit(ANWI.Messaging.Ops.ModifyUnit mod) {
			switch (mod.type) {
				case ANWI.Messaging.Ops.ModifyUnit.ChangeType.SetFlagship:
					fleet.SetFlagship(mod.unitUUID);
					PushToAll(new ANWI.Messaging.Ops.UpdateShip() {
						shipUUID = mod.unitUUID,
						type = ANWI.Messaging.Ops.UpdateShip.Type.SetFlagship
					});
					break;

				case ANWI.Messaging.Ops.ModifyUnit.ChangeType.SetWingCommander:
					fleet.SetWingCommander(mod.unitUUID);
					PushToAll(new ANWI.Messaging.Ops.UpdateWing() {
						type = ANWI.Messaging.Ops.UpdateWing.Type.ChangeWingCommander,
						boatUUID = mod.unitUUID
					});
					break;

				case ANWI.Messaging.Ops.ModifyUnit.ChangeType.ChangeName:
					fleet.SetWingName(mod.unitUUID, mod.str);
					PushToAll(new ANWI.Messaging.Ops.UpdateWing() {
						wingUUID = mod.unitUUID,
						type = ANWI.Messaging.Ops.UpdateWing.Type.SetName,
						str = mod.str
					});
					break;

				case ANWI.Messaging.Ops.ModifyUnit.ChangeType.ChangeCallsign:
					fleet.SetWingCallsign(mod.unitUUID, mod.str);
					PushToAll(new ANWI.Messaging.Ops.UpdateWing() {
						wingUUID = mod.unitUUID,
						type = ANWI.Messaging.Ops.UpdateWing.Type.SetCallsign,
						str = mod.str
					});
					break;

				case ANWI.Messaging.Ops.ModifyUnit.ChangeType.ChangeWingRole:
					fleet.ChangeWingRole(mod.unitUUID, (Wing.Role)mod.integer);
					PushToAll(new ANWI.Messaging.Ops.UpdateWing() {
						wingUUID = mod.unitUUID,
						type = ANWI.Messaging.Ops.UpdateWing.Type.ChangeRole,
						integer = mod.integer
					});
					break;
			}
		}
		#endregion

		#region Positions
		public void AddPosition(string unitUUID, List<int> roleID) {
			List<OpPosition> newPositions = new List<OpPosition>();

			foreach (int id in roleID) {
				OpPosition pos = new OpPosition() {
					uuid = ANWI.Utility.UUID.GenerateUUID(),
					unitUUID = unitUUID,
					critical = false,
					filledById = -1,
					filledByPointer = null,
					role = OperationRole.FetchById(id)
				};

				fleet.AddPosition(pos);
				newPositions.Add(pos);
			}

			PushToAll(new ANWI.Messaging.Ops.UpdatePositions(
				newPositions,
				null,
				null));
		}

		public void DeletePosition(string uuid) {
			fleet.DeletePosition(uuid);

			PushToAll(new ANWI.Messaging.Ops.UpdatePositions(
				null,
				null,
				new List<string>() { uuid }));
		}

		public void 
		ChangeAssignment(string posUUID, int userID) {
			if (posUUID == "" && userID != -1) {
				OpParticipant user = GetParticipant(userID);
				if (user == null)
					return;

				// Unassign this user
				if (user.position != null) {
					fleet.ClearPosition(user.position.uuid);

					PushToAll(new ANWI.Messaging.Ops.UpdateAssignments(
						null,
						new List<int>() { userID },
						null));
				}
			} else if (userID == -1 && posUUID != "") {
				// Unassign this position
				fleet.ClearPosition(posUUID);

				PushToAll(new ANWI.Messaging.Ops.UpdateAssignments(
					null,
					null,
					new List<string>() { posUUID }));
			} else {
				OpParticipant user = GetParticipant(userID);
				if (user == null)
					return;

				// Assign user to this position
				fleet.AssignPosition(posUUID, user);

				PushToAll(new ANWI.Messaging.Ops.UpdateAssignments(
					new List<System.Tuple<int, string>>() {
						new System.Tuple<int, string>(userID, posUUID),
					},
					null, null));
			}
		}

		public void SetPositionCritical(string uuid, bool crit) {
			OpPosition pos = fleet.GetPosition(uuid);
			if(pos != null) {
				pos.critical = crit;

				PushToAll(new ANWI.Messaging.Ops.UpdatePositions(
					null,
					new List<OpPosition>() { pos },
					null
					));
			}
		}
		#endregion

		private void PushToAll(ANWI.Messaging.IMessagePayload p) {
			MemoryStream stream = new MemoryStream();
			MessagePackSerializer.Get<ANWI.Messaging.Message>().Pack(
				stream, new ANWI.Messaging.Message(uuid, p));
			byte[] array = stream.ToArray();

			foreach (KeyValuePair<string, ConnectedUser> user in subscribed) {
				if(user.Value.socket != null)
					user.Value.socket.Send(array);
			}
		}
	}
}
