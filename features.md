## MonoDevelop.Debugger.Gdb.D Feature List

### Purpose of this file
This file contains a list of features to incorporate into the MonoDevelop Add-in as we go.
We use it to track the features to implement (road map) and also who and what is doing at the moment (assignments).

### How to use this document
There is no release version really ready for this Add-in currently, therefore issues are only regression bugs.
If you encounter such a regression, please create an issue directly on github for it in the project.
If anybody finds something is missing in the debugging experience with this Add-in and is able to specify it as a feature request,
please put it in here. We will manage the list to be reviewed and implemented regurarly.

### Features
It is a good practice that each feature has its corresponding breakpoint location in the test D project where it can be verified.
- associative arrays - values
- associative arrays - types
- complete class instance browsing support - values
- visualization of static class member data
- complete interface instance browsing support - values
- visualization of statically defined (global) data
- verify the stack visualization status
- navigate from stack frames into corresponding code
- visualize char values in rich format (char+hex+decimal)
- fix values for *string (aliased) variables (+ in arrays)
- use C# conversion for real numbers instead of our custom real interpretation of GDB memory
- *string aliases are misleadingly replaced by the unaliased immutable(*char)[]
  - in general, add type alias to variable type visualization, e.g. wstring[] (immutable(wchar)[][])
- get rid of these _TMPxx variables in Locals pad
- reformat dynamic string length xx from xx´"string" to something more appropriate, also to see the actual code point length (physical length) as well as the number of characters (logical length)
- strings visualized in text editor (text visualization pad)
- fix the Locals pad expansion visualization so that its content does not jump around each time a variable is expanded/collapsed
- verify the pointer variables resolution
- *string arrays do not have their types set, nor they can be expanded
- fix function parameters when instance of object/interface (not recognized at all)

-------
#### Notes
LL (llucenic) = Ľudovít Lučenič
AB (aBothe) = Alexander Bothe