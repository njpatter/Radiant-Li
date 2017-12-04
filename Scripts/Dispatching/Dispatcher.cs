// Messenger.cs v1.0 by Magnus Wolffelt, magnus.wolffelt@gmail.com
//
// Inspired by and based on Rod Hyde's Messenger:
// http://www.unifycommunity.com/wiki/index.php?title=CSharpMessenger
//
// This is a C# messenger (notification center). It uses delegates
// and generics to provide type-checked messaging between event producers and
// event consumers, without the need for producers or consumers to be aware of
// each other. The major improvement from Hyde's implementation is that
// there is more extensive error detection, preventing silent bugs.
//
// Usage example:
// Messenger<float>.AddListener("myEvent", MyEventHandler);
// ...
// Messenger<float>.Broadcast("myEvent", 1.0f);
using System;
using System.Collections.Generic;

public enum DispatchMode {
    DontRequireListener,
    RequireListener
}

static internal class DispatcherInternal {
    static public Dictionary<string, Delegate> eventTable = new Dictionary<string, Delegate>();
    static public readonly DispatchMode kDefaultMode      = DispatchMode.DontRequireListener;

    static public void OnListenerAdding(string eventType, Delegate listenerBeingAdded) {
        if (!eventTable.ContainsKey(eventType)) {
            eventTable.Add(eventType, null);
        }

        Delegate d = eventTable[eventType];
        if (d != null && d.GetType() != listenerBeingAdded.GetType()) {
            throw new ListenerException(string.Format("Attempting to add listener with inconsistent signature for event type {0}. Current listeners have type {1} and listener being added has type {2}", eventType, d.GetType().Name, listenerBeingAdded.GetType().Name));
        }
    }

    static public void OnListenerRemoving(string eventType, Delegate listenerBeingRemoved) {
        if (eventTable.ContainsKey(eventType)) {
            Delegate d = eventTable[eventType];

            if (d == null) {
                throw new ListenerException(string.Format("Attempting to remove listener with for event type {0} but current listener is null.", eventType));
            } else if (d.GetType() != listenerBeingRemoved.GetType()) {
                throw new ListenerException(string.Format("Attempting to remove listener with inconsistent signature for event type {0}. Current listeners have type {1} and listener being removed has type {2}", eventType, d.GetType().Name, listenerBeingRemoved.GetType().Name));
            }
        } else {
            throw new ListenerException(string.Format("Attempting to remove listener for type {0} but Messenger doesn't know about this event type.", eventType));
        }
    }

    static public void OnListenerRemoved(string eventType) {
        if (eventTable[eventType] == null) {
            eventTable.Remove(eventType);
        }
    }

    static public void OnBroadcasting(string eventType, DispatchMode mode) {
        if (mode == DispatchMode.RequireListener && !eventTable.ContainsKey(eventType)) {
            throw new DispatcherInternal.BroadcastException(string.Format("Broadcasting message {0} but no listener found.", eventType));
        }
    }

    static public BroadcastException CreateBroadcastSignatureException(string eventType) {
        return new BroadcastException(string.Format("Broadcasting message {0} but listeners have a different signature than the broadcaster.", eventType));
    }

    public class BroadcastException : Exception {
        public BroadcastException(string msg)
            : base(msg) {
        }
    }

    public class ListenerException : Exception {
        public ListenerException(string msg)
            : base(msg) {
        }
    }
}


// No parameters
static public class Dispatcher {
    private static Dictionary<string, Delegate> eventTable = DispatcherInternal.eventTable;

    static public void AddListener(string eventType, Callback handler) {
        DispatcherInternal.OnListenerAdding(eventType, handler);
        eventTable[eventType] = (Callback)eventTable[eventType] + handler;
    }

    static public void RemoveListener(string eventType, Callback handler) {
        DispatcherInternal.OnListenerRemoving(eventType, handler);   
        eventTable[eventType] = (Callback)eventTable[eventType] - handler;
        DispatcherInternal.OnListenerRemoved(eventType);
    }

    static public void Broadcast(string eventType) {
        Broadcast(eventType, DispatcherInternal.kDefaultMode);
    }

    static public void Broadcast(string eventType, DispatchMode mode) {
        DispatcherInternal.OnBroadcasting(eventType, mode);
        Delegate d;
        if (eventTable.TryGetValue(eventType, out d)) {
            Callback callback = d as Callback;
            if (callback != null) {
                callback();
            } else {
                throw DispatcherInternal.CreateBroadcastSignatureException(eventType);
            }
        }
    }
}

// One parameter
static public class Dispatcher<T> {
    private static Dictionary<string, Delegate> eventTable = DispatcherInternal.eventTable;

    static public void AddListener(string eventType, Callback<T> handler) {
        DispatcherInternal.OnListenerAdding(eventType, handler);
        eventTable[eventType] = (Callback<T>)eventTable[eventType] + handler;
    }

    static public void RemoveListener(string eventType, Callback<T> handler) {
        DispatcherInternal.OnListenerRemoving(eventType, handler);
        eventTable[eventType] = (Callback<T>)eventTable[eventType] - handler;
        DispatcherInternal.OnListenerRemoved(eventType);
    }

    static public void Broadcast(string eventType, T arg1) {
        Broadcast(eventType, arg1, DispatcherInternal.kDefaultMode);
    }

    static public void Broadcast(string eventType, T arg1, DispatchMode mode) {
        DispatcherInternal.OnBroadcasting(eventType, mode);
        Delegate d;
        if (eventTable.TryGetValue(eventType, out d)) {
            Callback<T> callback = d as Callback<T>;
            if (callback != null) {
                callback(arg1);
            } else {
                throw DispatcherInternal.CreateBroadcastSignatureException(eventType);
            }
        }
    }
}


// Two parameters
static public class Dispatcher<T, U> {
    private static Dictionary<string, Delegate> eventTable = DispatcherInternal.eventTable;

    static public void AddListener(string eventType, Callback<T, U> handler) {
        DispatcherInternal.OnListenerAdding(eventType, handler);
        eventTable[eventType] = (Callback<T, U>)eventTable[eventType] + handler;
    }

    static public void RemoveListener(string eventType, Callback<T, U> handler) {
        DispatcherInternal.OnListenerRemoving(eventType, handler);
        eventTable[eventType] = (Callback<T, U>)eventTable[eventType] - handler;
        DispatcherInternal.OnListenerRemoved(eventType);
    }

    static public void Broadcast(string eventType, T arg1, U arg2) {
        Broadcast(eventType, arg1, arg2, DispatcherInternal.kDefaultMode);
    }

    static public void Broadcast(string eventType, T arg1, U arg2, DispatchMode mode) {
        DispatcherInternal.OnBroadcasting(eventType, mode);
        Delegate d;
        if (eventTable.TryGetValue(eventType, out d)) {
            Callback<T, U> callback = d as Callback<T, U>;
            if (callback != null) {
                callback(arg1, arg2);
            } else {
                throw DispatcherInternal.CreateBroadcastSignatureException(eventType);
            }
        }
    }
}


// Three parameters
static public class Dispatcher<T, U, V> {
    private static Dictionary<string, Delegate> eventTable = DispatcherInternal.eventTable;

    static public void AddListener(string eventType, Callback<T, U, V> handler) {
        DispatcherInternal.OnListenerAdding(eventType, handler);
        eventTable[eventType] = (Callback<T, U, V>)eventTable[eventType] + handler;
    }

    static public void RemoveListener(string eventType, Callback<T, U, V> handler) {
        DispatcherInternal.OnListenerRemoving(eventType, handler);
        eventTable[eventType] = (Callback<T, U, V>)eventTable[eventType] - handler;
        DispatcherInternal.OnListenerRemoved(eventType);
    }

    static public void Broadcast(string eventType, T arg1, U arg2, V arg3) {
        Broadcast(eventType, arg1, arg2, arg3, DispatcherInternal.kDefaultMode);
    }

    static public void Broadcast(string eventType, T arg1, U arg2, V arg3, DispatchMode mode) {
        DispatcherInternal.OnBroadcasting(eventType, mode);
        Delegate d;
        if (eventTable.TryGetValue(eventType, out d)) {
            Callback<T, U, V> callback = d as Callback<T, U, V>;
            if (callback != null) {
                callback(arg1, arg2, arg3);
            } else {
                throw DispatcherInternal.CreateBroadcastSignatureException(eventType);
            }
        }
    }
}

// Four parameters
static public class Dispatcher<T, U, V, W> {
	private static Dictionary<string, Delegate> eventTable = DispatcherInternal.eventTable;
	
	static public void AddListener(string eventType, Callback<T, U, V, W> handler) {
		DispatcherInternal.OnListenerAdding(eventType, handler);
		eventTable[eventType] = (Callback<T, U, V, W>)eventTable[eventType] + handler;
	}

	static public void RemoveListener(string eventType, Callback<T, U, V, W> handler) {
		DispatcherInternal.OnListenerRemoving(eventType, handler);
		eventTable[eventType] = (Callback<T, U, V, W>)eventTable[eventType] - handler;
		DispatcherInternal.OnListenerRemoved(eventType);
	}
	
	static public void Broadcast(string eventType, T arg1, U arg2, V arg3, W arg4) {
		Broadcast(eventType, arg1, arg2, arg3, arg4, DispatcherInternal.kDefaultMode);
	}
	
	static public void Broadcast(string eventType, T arg1, U arg2, V arg3, W arg4, DispatchMode mode) {
		DispatcherInternal.OnBroadcasting(eventType, mode);
		Delegate d;
		if (eventTable.TryGetValue(eventType, out d)) {
			Callback<T, U, V, W> callback = d as Callback<T, U, V, W>;
			if (callback != null) {
				callback(arg1, arg2, arg3, arg4);
			} else {
				throw DispatcherInternal.CreateBroadcastSignatureException(eventType);
			}
		}
	}
}