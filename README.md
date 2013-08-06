## GDB front-end for D programming language
This [MonoDevelop](http://www.monodevelop.com) add-in - MonoDevelop.Debugger.Gdb.D - provides debugging support for [D programming language](http://dlang.org) in MonoDevelop IDE under GNU/Linux. This project depends on the D Language Binding add-in for MonoDevelop - the so called [Mono-D](http://mono-d.alexanderbothe.com) project and on its [D Parser](https://github.com/aBothe/D_Parser) part.

## General information
This project aims to enhance the user interface of Mono-D with rich debugging capabilities.
MonoDevelop.Debugger.Gdb.D add-in adds exacter visualization for values and types of variables available in the Locals Pad of the MonoDevelop workbench during a debugging session.
Latest version of the D2 language is always supported. Support for any of the previous versions is not guaranteed until release stage.
The add-in contains and further derives code of the basic GDB Debugger Support integrated into MonoDevelop, though it does not provide more functionality that the GDB back-end does provide.

#### History of the project
The project began on the verge of year 2013. The major initial work (kind of proof-of-concept) took place from late January 2013 through the end of February 2013. Since then, the project was restructured in order to support both the 32-bit as well as the 64-bit architectures. Besides of this, much of the redundant code has been left out. As a result of this, some regressions might have occured, fixing of which is on the way. In August 2013, the work is being resumed to move the add-in further to at least a beta stage.

#### Current status
Momentarily, the add-in is in its earlier alpha stage. Basic concepts are more or less understood. The project was successfully built as a MonoDevelop plugin and put to the repository to install from (see below). Features that work so far are at least these (if you find anything of the following does not work, please report an issue):
- variables of all unaliased primitive types defined locally within the breakpointed scope or as a routine arguments have their types and values correctly resolved (bool, byte, ubyte, short, ushort, int, uint, long, ulong, char, wchar, dchar, float, double and real)
- unary and n-ary arrays of unaliased primitive types are resolved as well,
- *char-array types and *string types (char[], wchar[], dchar[], string, wstring and dstring) are visualized correctly together with their physical length, the string/array elements identify multi-code points as well as skipped code points (applicable for char[] and string only)
- inherited and native member variables of primitive types are listed correctly in an object variable (i.e. an instance of a class) or through 'this' variable within the code of the instance's class
- pointers to primitive types are translated only to the value they point to
- instances of classes should have resolved their dynamic types and such a variable displays the output of the instance's call of the toString() method as its value
- stack frame should be demangled correctly with argument values visualized to an extent

The preliminary roadmap and known issues (list of features and bugs) are to be found in [features.md](features.md) file in the project. Also there may be some issues with the debugging (stepping through a program) which can be caused by compiler's backend (optimisation) - sometimes the debugging caret jumps over the code in a not so obvious manner. This seems to be true especially for test code. The more we test it on real projects the better the results will be in the end :-)

### How to install
Having MonoDevelop version 4.0 or higher is a prerequisite.

For now, in order to use the add-in, please get the version accessible through MonoDevelop repository for Mono-D.  
Set up the `http://mono-d.alexanderbothe.com/main.mrep` repository location in Manage repositories... option within Add-in Manager Gallery in MonoDevelop. Then (if you don't already have it) install 'D Language Binding' (i.e. Mono-D) and then 'D Language Debugging Support under GNU/Linux (GDB)' (i.e. MonoDevelop.Debugger.Gdb.D) add-ins. Enjoy !

### How to build
There is to be a release branch soon in this repository, that should suffice for anyone to clone the repository locally, open the [MonoDevelop.Debugger.Gdb.D.sln](MonoDevelop.Debugger.Gdb.D.sln) solution file in MonoDevelop IDE and then build the project.
If it currently does not suffice to have sources only of this project, while having Mono-D only in binaries, please download or clone the Mono-D sources from [Alexander's repository on GitHub](https://github.com/aBothe/Mono-D).

Please help us improve this information by reporting any inconveniences you encounter while building this project. Thank you !

### Help to develop
In order to lend a hand in development, it is necessarry to have recursively (i.e. with submodules) cloned these git repositories
- [monodevelop](https://github.com/mono/monodevelop) - optional, though recommended,
- [Mono-D](https://github.com/aBothe/Mono-D) and
- [MonoDevelop.Debugger.Gdb.D](https://github.com/llucenic/MonoDevelop.Debugger.Gdb.D)

On terminal, run:  
`sudo apt-get install git`  
`sudo apt-get build-dep monodevelop`  
`mkdir -p ~/work/git`  
`cd ~/work/git`  
`git clone --recursive https://github.com/mono/monodevelop.git`  
`git clone --recursive https://github.com/aBothe/Mono-D.git`  
`git clone https://github.com/llucenic/MonoDevelop.Debugger.Gdb.D.git`  

It is also necessarry to have mono version 2.10.9 or later present in your system.
Those of you running Ubuntu 12.04 (Precise) or previous versions or not having this or newer version of mono in your package repositories have to build mono from sources or use [the version](http://simendsjo.me/files/abothe) irregularly built and published by [Alexander Bothe](http://mono-d.alexanderbothe.com).

We recommend you start by using the pre-compiled version of mono and monodevelop (from the [abovementioned](http://simendsjo.me/files/abothe) Alexander's repository).
You extract the archive corresponding to your architecture into root, so you get its content under `/opt/mono`.

Then run the MonoDevelop on terminal  
`/opt/mono/bin/monodevelop &`  

Now, create your own workspace, add the solutions for Mono-D and MonoDevelop.Debugger.Gdb.D in it.
Then, build the Mono-D add-in and the MonoDevelop.Debugger.Gdb.D add-in. Please report any problem you might have as this guide is written ex-post.

Finally, install your versions of add-ins into the pre-compiled version of monodevelop by creating symlinks into its location:
`ln -s ~/work/git/MonoDevelop.Debugger.Gdb.D/build /opt/mono/lib/monodevelop/AddIns/MonoDevelop.Debugger.Gdb.D`  
`ln -s ~/work/git/Mono-D/MonoDevelop.DBinding/bin/Debug/D_Parser.dll /opt/mono/lib/monodevelop/AddIns/BackendBindings/`  
`ln -s ~/work/git/Mono-D/MonoDevelop.DBinding/bin/Debug/D_Parser.dll.mdb /opt/mono/lib/monodevelop/AddIns/BackendBindings/`  
`ln -s ~/work/git/Mono-D/MonoDevelop.DBinding/bin/Debug/MonoDevelop.D.dll /opt/mono/lib/monodevelop/AddIns/BackendBindings/`  
`ln -s ~/work/git/Mono-D/MonoDevelop.DBinding/bin/Debug/MonoDevelop.D.dll.mdb /opt/mono/lib/monodevelop/AddIns/BackendBindings/`  
`ln -s ~/work/git/Mono-D/MonoDevelop.DBinding/bin/Debug/Newtonsoft.Json.dll /opt/mono/lib/monodevelop/AddIns/BackendBindings/`  

Then restart MonoDevelop, open your created workspace or just the solution file for MonoDevelop.Debugger.Gdb.D and have fun !

If you have any questions regaring the add-in or the build procedure or on development, feel free to contact us (`skype-id:moondog82`,`icq:117-149-616`).

Kind regards from  
Ľudovít and Alexander
