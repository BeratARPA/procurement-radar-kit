' Runs a console command with no window, and exits with its exit code.
'
' Why this exists. The worker and the api are console applications and the task
' has to run them in the logged-on session - D-10, because Docker Desktop lives
' in that session and the database lives in Docker. A console application in an
' interactive session gets a console window, so two of them sat on the desktop
' permanently. They are not decoration: closing one kills that program, which is
' a collector one stray click away from stopping.
'
' Why not the obvious fixes.
'
'   * A non-interactive logon type (S4U) hides the window and puts the process
'     in a session where Docker Desktop is not running, so the worker starts and
'     finds no database. That is D-10's whole argument, in reverse.
'   * OutputType=WinExe hides it too, and permanently: running the exe by hand
'     in a terminal would print nothing, ever. This wrapper leaves both programs
'     ordinary console applications, so that still works.
'
' The waiting is load-bearing, not incidental. Run(cmd, 0, True) blocks until
' the command returns, so the task instance stays alive for as long as the
' worker does - which is what MultipleInstances=IgnoreNew needs to suppress the
' five-minute watchdog tick. A fire-and-forget wrapper (Start-Process without
' -Wait) would let the task complete immediately, and the next tick would start
' a second worker: two collectors sharing one politeness gate, which is how an
' IP gets banned.
'
' The exit code is the point of the last line. Run returns what the command
' returned and Quit puts it back where Task Scheduler reads it. A wrapper that
' reported its own success would make every failed run look clean, and the
' result code is the only evidence the watchdog leaves in the task history.
Option Explicit

Dim shell, command, i

If WScript.Arguments.Count = 0 Then
    WScript.Quit 2
End If

Set shell = CreateObject("WScript.Shell")

' Quoted only where it is needed. Quoting everything looks safer and is not:
' cmd.exe ""/c"" does not read as a switch, so a wrapper that quoted every
' argument turned a command returning 7 into one returning 1 - measured in the
' sibling project, which is where this file comes from.
Dim argument
command = ""
For i = 0 To WScript.Arguments.Count - 1
    argument = WScript.Arguments(i)

    If InStr(argument, " ") > 0 Then
        argument = """" & argument & """"
    End If

    If i > 0 Then command = command & " "
    command = command & argument
Next

WScript.Quit shell.Run(command, 0, True)
