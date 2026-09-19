using System;
using System.Collections.Generic;
using System.Linq;
using GymQ.Models;

namespace GymQ.SessionModule
{
    /// <summary>
    /// Why a session ended — useful for reporting and for debugging test failures
    /// (e.g. "did this end because of a nudge, or the time cap?").
    /// </summary>
    public enum SessionEndReason
    {
        ManualFinish,
        NudgeResponse,
        NudgeTimeout,
        MaxDurationReached
    }

    /// <summary>
    /// Represents one member's usage session on a piece of equipment,
    /// from claiming their turn to the session ending.
    /// </summary>
    public class UsageSession
    {
        public string SessionId { get; private set; }
        public string EquipmentId { get; private set; }
        public string MemberId { get; private set; }
        public DateTime StartTime { get; private set; }
        public DateTime? EndTime { get; private set; }
        public SessionEndReason? EndReason { get; private set; }
        public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;

        // UsageSession Constructor
        public UsageSession(string sessionId, string equipmentId, string memberId, TimeProvider? clock = null)
        {
            SessionId = sessionId;
            EquipmentId = equipmentId;
            MemberId = memberId;
            
            var timeSource = clock ?? TimeProvider.System;

            StartTime = timeSource.GetUtcNow().UtcDateTime;
        }

        // MarkEnded Method:
        // Act as a helper method for EndSession which helps
        // also can be useful for tracking records for reporting.
        internal void MarkEnded(DateTime endTime, SessionEndReason reason)
        {
            EndTime = endTime;
            EndReason = reason;
        }
    }

    public class SessionService
    {
        // In-memory list for newly created session
        private readonly List<UsageSession> _sessions = new(); 
        // Storage for equipments.
        private readonly Dictionary<string, Equipment> _equipment;
        // It uses for recording the actual time whenever a session starts/ends
        private readonly TimeProvider _clock;
        // Ensures one that only one thread can execute in a specific block of code.
        private readonly object _lockObject = new();

        // SessionService Constructor
        public SessionService(Dictionary<string, Equipment> equipment, TimeProvider? clock = null)
        {
            _equipment = equipment;
            _clock = clock ?? TimeProvider.System;
        }
        
        // StartSession() method:
        // Allows members to start their session when it is 
        // their turn in the queue. Which it also tracks
        // who uses the machine, what machine is being used,
        // starting time of the session. 
        public UsageSession StartSession(string equipmentId, string memberId)
        {
            // Ensures only one thread can start session at a time (race conditions)
            lock (_lockObject)
            {
                // Validate memberId
                if (string.IsNullOrEmpty(memberId))
                {
                    throw new ArgumentException("Member ID must not be null or empty.", nameof(memberId));
                }
                
                // Validate equipmentId
                if (string.IsNullOrEmpty(equipmentId))
                {
                    throw new ArgumentException("Equipment ID must not be null or empty.", nameof(equipmentId));
                }

                // Finds an equipment via equipmentId through _equipment if not throws exception 
                if (!_equipment.TryGetValue(equipmentId, out var equipment))
                {
                    throw new ArgumentException(
                        $"Equipment '{equipmentId}' not found in the system.", nameof(equipmentId));
                }

                // Check for any active sessions via equipmentId through _sessions
                bool alreadyActive = _sessions.Exists(s =>
                s.EquipmentId == equipmentId && s.EndTime == null);
                
                if(alreadyActive)
                {
                    throw new InvalidOperationException(
                        $"Equipment {equipmentId} already has an active session. " +
                        "EndSession must be called before starting a new one.");

                }

                // Checks if equipment status is available or not.
                if(equipment.Status == EquipmentStatus.Unavailable)
                {
                    throw new InvalidOperationException("This equipment is unavailable, due to maintenance");
                }

                // Change equipment status to InUse
                equipment.Status = EquipmentStatus.InUse;

                // Create a session
                var session = new UsageSession(

                    Guid.NewGuid().ToString(),
                    equipmentId,
                    memberId,
                    _clock

                );

                // Add the new session to _sessions
                _sessions.Add(session);

                // return to the caller.
                return session;
            }
        }

        // EndSession() method:
        // This function allows member to end their session
        // and records the time when it was ended 
        // and the reason why it ended. (e.g. Nudge response, MaxDurationReached, etc.)
        public void EndSession(string equipmentId, SessionEndReason reason)
        {
            
            lock (_lockObject)
            {
                // Validate equipmentId
                if (string.IsNullOrEmpty(equipmentId))
                {
                    throw new ArgumentException("Equipment ID must not be null or empty.", nameof(equipmentId));
                }

                // Find the session via equipmentId throug _sessions
                var session = _sessions.Find(s =>
                    s.EquipmentId == equipmentId && s.EndTime == null);

                // Checks if the session does not exists
                if(session == null)
                {
                    throw new InvalidOperationException(
                        $"No active session found for equipment '{equipmentId}'. " +
                        "Session may have already been ended or equipment was never in use." 
                    );
                }

                // Mark the session as ended and also records the time when it was ended, and its reason
                session.MarkEnded(_clock.GetUtcNow().UtcDateTime, reason);

                // Find the key (equipmentId)
                if(_equipment.TryGetValue(equipmentId, out var equipment))
                {   
                    // If found, check if the equipment is not Unavailable
                    if(equipment.Status != EquipmentStatus.Unavailable)
                    {   
                        // Change the equipment status to Available
                        equipment.Status = EquipmentStatus.Available;
                    }
                }
            }
            
        }
        
        // EnforceMaxSessionDuration() method:
        // This maps to FR-008 which system enforces a 30 minutes
        // maximum session duration for each member's on queue
        // which it EndSession automatically after 30 minutes.
        public void EnforceMaxSessionDuration(string equipmentId)
        {
            lock (_lockObject)
            {
                // Validate equipmentId
                if (string.IsNullOrEmpty(equipmentId))
                {
                    throw new ArgumentException("Equipment ID must not be null or empty.", nameof(equipmentId));
                }

                // Find session via equipmentId throug _sessions
                var session = _sessions.Find(s =>
                    s.EquipmentId == equipmentId && s.EndTime == null);

                // Checks if the session does not exist
                if(session == null)
                {
                    return;
                }

                // This simple time equation calculates checks if the session time is more than 30 minutes.
                // _clock.GetUtcNow().UtcDateTime gets the actual time and substract it to the start time 
                // of the session which gets the total session time and checks if it already exceeds 30 minutes.
                if(_clock.GetUtcNow().UtcDateTime - session.StartTime >= TimeSpan.FromMinutes(30))
                {
                    // Ends the session if it exceeds the maximum session duration.
                    EndSession(equipmentId, SessionEndReason.MaxDurationReached);
                }
            }
            
        }

        // GetSessionDuration() method:
        // Shows the duration 
        public TimeSpan? GetSessionDuration(string sessionId)
        {
            // Looks up for every SessionId from _sessions with sessionId that we're looking for.
            var session = _sessions.Find(s => s.SessionId == sessionId);

            return session?.Duration; // session duration
        }

        // GetAllEquipmentStatus() method: 
        // This method aligns with FR-009 which allows admin
        // to view current status of all equipment where it
        // sits in the admin dashboard.
        public List<Equipment> GetAllEquipmentStatus()
        {   
            // Displays every equipment on the list
            return _equipment.Values.ToList();
        }
    }
}
