#NoTrayIcon
#SingleInstance Force
SetWorkingDir %A_ScriptDir%
SendMode Input
SetKeyDelay -1, -1

signalFile := "explode.signal"

; ---- WAIT MODE ----
Loop
{
    if FileExist(signalFile)
        break
    Sleep, 10
}

; ---- FORCE FOCUS SUBNAUTICA ----
WinActivate, ahk_exe Subnautica.exe
WinWaitActive, ahk_exe Subnautica.exe, , 2

; ---- HOLD ESCAPE ----
SendInput, {Esc down}
Sleep, 1550
SendInput, {Esc up}

FileDelete, %signalFile%
ExitApp