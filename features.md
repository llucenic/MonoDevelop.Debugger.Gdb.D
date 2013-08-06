## List of Features

### Purpose of this document
This file contains a list of features, improvements and known issues to incorporate into the MonoDevelop Add-in as we go. We use it to track the features to implement (road map) and also who and what is doing at the moment (assignments).

### How to use this document
There is no release version really ready for this Add-in currently, therefore issues are only regression bugs.
If you encounter such a regression, please create an issue directly on github for it in the project.
If anybody finds something is missing in the debugging experience with this Add-in and is able to specify it as a feature request, please put it in here. We will manage the list to be reviewed and implemented regularly.

### Features
It is a good practice that each feature has its corresponding breakpoint location in the test D project where it can be verified.
- **LL** associative arrays - values
- **LL** associative arrays - types
- **LL** complete class instance browsing support - values
- **LL** visualization of static class member data
- **LL** complete interface instance browsing support - values
- **LL** visualization of statically defined (global) data
- **LL** strings visualized in text editor (text visualization pad)
- **LL** support for browsing struct instances

### Improvements
- **LL** verify the stack visualization status
- **LL** navigate from stack frames into corresponding code
- **LL** visualize char values in rich format (char+hex+decimal)
- **LL** use C# conversion for real numbers instead of our custom real interpretation of GDB memory
- **LL** *string aliases are misleadingly replaced by the unaliased immutable(*char)[]
  - **LL** in general, add type alias to variable type visualization, e.g. wstring[] (immutable(wchar)[][])
- **LL** get rid of these _TMPxx variables in Locals pad
- **LL** reformat dynamic string length xx from xx´"string" to something more appropriate, also to see the actual code point length (physical length) as well as the number of characters (logical length)
- **LL** verify the pointer variables resolution

### Known issues
- **AB** fix values for *string (aliased) variables (+ in arrays)
- **LL** fix the Locals pad expansion visualization so that its content does not jump around each time a variable is expanded/collapsed
- **LL** fix function parameters when instance of object/interface (not recognized at all)
- **AB** *string arrays do not have their types set, nor they can be expanded

-------
#### Notes
**LL** (llucenic) = Ľudovít Lučenič  
**AB** (aBothe) = Alexander Bothe
