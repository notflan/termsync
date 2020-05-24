# Terminal Synchroniser

Termsync is a **.NET Core** library for synchronising terminal IO in an asynchronous contex.
It guarantees user keystrokes into the console are never split up by `WriteLines()`s happening on other threads.
Also exposed is a global `Channel<string>` object for reading user input lines asynchronously.

It spawns a "worker" `Task` when initialised that creates channels for piping info to and from the console atomically.

## Documentation

See [Terminal.cs][terminal-cs] for inline API documentation. (WIP)

[terminal-cs]: ../blob/master/termsync/Terminal.cs

## Examples

### Initialising the global state
Start the worker that reads and writes to `Console`.
``` c#
	Task worker = Terminal.Initialise();
	
	// ... program code here
	
	Terminal.Close(); // Manually clean up resources. Not required as it is cleaned up on exist.
	await worker; // Wait for the worker to close if you want.
```

### Write lines
Line writes are asynchronous, await them to yield until the line has been displayed in the `Console`.
``` c#
	await Terminal.WriteLine("A line.")
```
### Read lines
Line reads can take `CancellationToken`.
``` c#
	string line = await Terminal.ReadLine(source.Token);
```

### Change user prompt
User prompt can be changed. Await `ChangePrompt` to yield until the new prompt has been displayed.

``` c#
	await Terminal.ChangePrompt("USER> ");
	
	await Terminal.ChangePrompt(""); //No prompt!
```

### Other control
Multiple threads might often want to write multiple lines over a short period of time, and not have them messed up with another line(s) from other threads.
The programmer might encounter a situation like this:

1. Thread 1
  - `WriteLine("Line 1 of important message");`
  - `WriteLine("Line 2 that we don't want");`
  - `WriteLine("Line 3 to be split up by another message");`
2. Thread 2
  - `WriteLine("Line 4 of thread 2's important message");`
  - `WriteLine("Line 5 that it really doesn't want");`
  - `WriteLine("Line 6 to be split up by another message");`
3. Thread 3
  - `WriteLine("A single line message from thread 3");`

And she could see this:
```
	Line 1 of important message
	Line 4 of thread 2's important message
	Line 2 that we don't want
	Line 3 to be split up by another message
	Line 5 that it really doesn't want
	A single line message from thread 3
	Line 6 to be split up by another message
```

Oh no! This is quite annoying. For this we have 2 APIs.

#### Lock
`Lock` acquires the global write mutex as long as the lock handle is alive. You can write to the lock handle as you would to `Terminal`.

``` c#
	using(var terminal = await Terminal.Lock()) // Lock is asynchronously acquired here.
	{
		await terminal.WriteLine("Line 1");
		await termianl.WriteLine("Line 2");
		await terminal.WriteLine("Line 3");
	} // The lock is released when `terminal`'s `Dispose()` method is called here.
```
These writes are now guaranteed to not be interrupted by other calls to `WriteLine` anywhere within `Terminal`.
The drawback is it is a global mutex that can be acquired for any amount of time, potentially blocking other tasks where it wouldn't be ideal. For these situations there is `Stage`.

#### Stage
`Stage` stores the writes in a buffer temporarily. The only mutex it acquires is internal to the stage handle, and doesn't hold the global write mutex until it wants to push everything it has stored.

``` c#
	await using(var terminal = Terminal.Stage()) // Create a new `Stage` here.
	{
		await terminal.WriteLine("Line 4"); // These are stored in `Stage` until its `DisposeAsync()` method is called.
		await terminal.WriteLine("Line 5");
		await terminal.WriteLine("Line 6");
	} // All buffered lines are written atomically here.
```

## Pitfalls
* The library respectfully asks the programmer to use the `Terminal` interface instead of the `Console` interface for all reads and writes in the program. I realise this is not ideal, or even possible in some cases.
  This can cause problems when dependencies write to `Console` and can break lines and such. 
  Maybe I will try to fix this if I run into this problem.
* Currently only full line writes are supported at a time.
  I will likely fix this in the future.

## License
GPL'd with love <3
