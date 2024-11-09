# Sylver Ink: An Exercise in Brainrot

Several to many 'sticky note' applications exist for Windows, including one built into the very operating system. But none of these applications are particularly wieldy for the neurodivergent brain, which necessitates the keeping of multiple notebooks all containing hundreds of blank pages with the occasional single line of text somewhere in the haystack.

Sylver Ink is designed to streamline and simplify the ADHD brain process, a Sisyphean task at which humanity has spent fifty millennia collectively failing. With the aid of Sylver Ink, users can organize their sticky notes into one or more databases; sort them by content, creation, and last change; and display them in helpful mini-windows which snap to each other like honeycomb in a hive.

**Sylver Ink is currently in an early beta release.** Check frequently for updates!

## Usage

Upon running Sylver Ink for the first time, the default database will be automatically created in the user's My Documents folder, blank and ready to be impregnated.

### Introduction

The most prominent feature of the main window is the Recent Notes box. This list box displays a sequence of note entries sorted by, depending on user settings, the date of the note's creation or the date the note was last modified.

New notes can be created by entering them in the New Notes textbox near the bottom of the screen.

At the top of the window is the application's context bar, from which note databases can be opened, closed, deleted, or created. Options are also provided for viewing the properties of the current database, and for connecting to networked databases or opening the current database to connections.

Below the context bar are two ribbons: The top displays the user's currently open databases; the bottom displays the user's open notes in the current database.

Opening a note will create a tiny sticky note window from which the note can be quickly viewed or edited. These note windows snap to each other for easy organization, and also provide a "View" button that will open the note in a larger tab within the main window. This tab provides access to previous versions of the note, which Sylver Ink saves along with the current version.

### "Import"

The Import window allows the user to import multiple notes from plaintext files. Sylver Ink divides newly imported notes based on the number of empty lines between paragraphs in the text file. The Import window may also be used to import existing Sylver Ink databases, allowing the user to overwrite their currently open database or merge the two.

### "Replace"

The Replace window allows the user to mass-replace occurrences of a text string with another across the entire database. Care must be taken when using this function, as it cannot be automatically undone; if a mistake is made while replacing, the user may close the database or the program and reopen it to prevent the change from being saved.

### "Search"

The Search window allows the user to search for occurrences of a text string across the database, and display the results in a list. Sylver Ink uses a tagging system to assist in sorting search results: Notes are prioritized if they match words in the search query with an overall low occurrence rate in the database at large.

### "Settings"

The Settings window provides options for customizing the user's experience; placing sticky notes always on top, sorting note entries, and customizing the colors and visual style of the Sylver Ink interface are all options provided in this window.

## Contributions

- [Taica](https://github.com/taicanium/) (code base, frontend design)
- N. Hunter (backend debugging and testing)
- [Miles Farber](https://github.com/milesfarber/) (therapy)
