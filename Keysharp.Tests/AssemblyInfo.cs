// In SDK-style projects such as this one, several assembly attributes that were historically
// defined in this file are now automatically added during build and populated with
// values defined in project properties. For details of which attributes are included
// and how to customise this process see: https://aka.ms/assembly-info-properties


// Setting ComVisible to false makes the types in this assembly not visible to COM
// components.  If you need to access a type in this assembly from COM, set the ComVisible
// attribute to true on that type.

[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM.

[assembly: Guid("cc70a763-b0a6-4586-bba5-a37a2fe0ea3b")]

// Set a fixed culture to prevent problems caused by differing user cultures
[assembly: SetCulture("en-US")]

// The suite shares process-global runtime state such as Script.TheScript, compiler output,
// console redirection, and the hosted Eto UI loop. Running tests concurrently in one process
// is not safe.
[assembly: LevelOfParallelism(1)]
