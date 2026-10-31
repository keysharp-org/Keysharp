; A_LineFile stays anchored to this library when the test body is compiled from text.
TestFixturePath(_RelativePath := "")
{
	SplitPath(A_LineFile, , &_LibraryDirectory)
	SplitPath(_LibraryDirectory, , &_FixtureDirectory)
	return _FixtureDirectory "/" _RelativePath
}
