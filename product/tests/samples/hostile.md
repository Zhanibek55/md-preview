# Hostile content test

Nothing below may run a script, reach the network, open a browser on its own or read files outside this folder.
If this page shows the heading and the list, and the log shows no external requests, the test passes.

<script>document.body.innerHTML = '<h1>SCRIPT RAN</h1>';</script>

<img src="https://example.com/tracker.png" alt="remote image (must be blocked)">

<img src="x" onerror="document.body.innerHTML='<h1>ONERROR RAN</h1>'" alt="onerror handler (must not run)">

<iframe src="https://example.com"></iframe>

<meta http-equiv="refresh" content="0;url=https://example.com/auto-redirect">

<form action="https://example.com/steal"><input name="q" value="secret"><button>Submit</button></form>

- [javascript link](javascript:alert(1))
- ![path traversal](../../../../Windows/win.ini)
- ![absolute path](C:/Windows/win.ini)
- <a href="file:///C:/Windows/win.ini">file link</a>
