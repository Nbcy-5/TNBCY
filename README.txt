TNBCY (Godot 4 .NET) - setup
1. Put this folder at C:\TNBCY02 (delete any old C:\TNBCY02 first).
2. Match the SDK version: open TNBCY.csproj and make "Godot.NET.Sdk/4.3.0" equal to YOUR Godot version
   (shown in the Godot title bar), e.g. 4.4.1.
3. Open Godot > Import > C:\TNBCY02\project.godot. Press Play (F5) to test on PC.
4. For Android: Editor > Manage Export Templates > Download. Editor Settings > Export > Android:
   set Android SDK Path (C:\Android) and Java SDK Path (your Eclipse Adoptium JDK folder).
5. Make the debug keystore once (cmd):
   keytool -keyalg RSA -genkeypair -alias androiddebugkey -keypass android -keystore "%APPDATA%\Godot\keystores\debug.keystore" -storepass android -dname "CN=Android Debug,O=Android,C=US" -validity 9999 -deststoretype pkcs12
6. Then export from cmd with update.bat (edit the GODOT path inside it).
