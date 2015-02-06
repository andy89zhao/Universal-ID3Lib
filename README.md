# Universal ID3 Lib
Portable library for developing universal apps.
Supports ID3v2.3.0

##Installation
Using nuget package manager:
PM> Install-Package UniversalID3Lib

https://www.nuget.org/packages/UniversalID3Lib/

##Examples
###First create an object:
```
ID3 id3 = new ID3();
```
###Read the tags from the file:
```
await id3.GetMusicPropertiesAsync(file);
```
Here "file" is a "StorageFile".
####Now simply get the properties:
string title = id3.Title;
string album = id3.Album;
string artist = id3.Artist;
int rating = id3.rating;    // 0 to 5

```
// returns null if not found
BitmapImage albumArt = await id3.GetAlbumArtAsync();
```

###Save the tags to the file:
First initialize the id3 object:
```
ID3 id3 = new ID3();
await id3.GetMusicPropertiesAsync(file);
```
Then set the properties:
```
id3.Title = "title";
id3.album = "album";
// setting all properties is not required

// optionally
await id3.SetThumbnailAsync(file); // here file is .jpeg file.

// Finally save the tags:
await id3.SaveMusicPropertiesAsync();
```

##Caution
Use one ID3 object for each file. (Don't reuse it.)
