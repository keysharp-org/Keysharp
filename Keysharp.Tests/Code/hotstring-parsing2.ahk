#NoTrayIcon

::bitw::biggest in the world
; 1 -> :2
::1:::2
; 3 -> ::4
::3::::4
; 5: -> 6
::5`:::6
; 7: -> :8
::7`::::8

; Quotes are ordinary characters in a hotstring's trigger and replacement.
::arn't::aren't
::i"m::I'm
::charge d'affaires::charge d'affaires
::let's it::lets it
:*:i"q::I "quote"

; A ';' starts a comment only when whitespace precedes it.
:?:; btu::; but
::bt;w::by the; way
::btw::by the way ; basic hotstring

; '{' opens a block body only when it is the last thing on the line.
::sig::{Enter}Regards

; A hotstring inside a plain block — the grouping commonly wrapped around a #HotIf section.
{
::inblock::in a block
}

::text1::
(
Any text between the top and bottom parentheses is treated literally.
By default, the hard carriage return (Enter) between the previous line and this one is also preserved.
    By default, the indentation (tab) to the left of this line is preserved.
)

myfunc()
{
}

:X:mf1::myfunc

:X:mf2::{
  myfunc
}

:X:mf3::
{
  myfunc
}

::mf4::{
  myfunc
}

::mf5::
{
  myfunc
}

#Hotstring X

::mf6::myfunc
:X:mf7::myfunc
::mf8::myfunc

; Raw/text mode rules out a block body, so even a lone '{' is replacement text.
#Hotstring X0
:T:brace::{

FileAppend "pass", "*"

ExitApp()
