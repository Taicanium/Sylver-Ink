# Header

Signature -- "SYL " [4 bytes: 0x53 0x59 0x4C 0x20]

Format number -- [1 byte]



# Format schemas

- Format 1

```
Record count -- [4 bytes: big-endian unsigned int]
	Record creation date/time -- [4 bytes: big-endian unsigned int, length of following string][_n_ bytes: string from long]
	Record index -- [4 bytes][_n_ bytes: string from signed int]
	Record initial state -- [4 bytes][_n_ bytes: string. Initial text of the record at the time of creation]
	Record modified date/time -- [4 bytes][_n_ bytes: string from long. Date and time of the last change to record]
	Revision count -- [4 bytes][_n_ bytes: string from signed int. Number of changes made to record]
		Revision creation date/time -- [4 bytes][_n_ bytes: string from long. Date the change was made]
		Revision start index -- [4 bytes][_n_ bytes: string from signed int. Index of the change to the record string. Dependant on the state of the record after reconstruction from all previous revisions (see NoteRecord.Reconstruct)]
		Revision substring -- [4 bytes][_n_ bytes: string. The text to insert in the record after removing all text after the revision start index. May be string.Empty]
```

- Format 2

Data is compressed using the Lempel–Ziv–Welch (LZW) algorithm.

The LZW bit stream is formatted with a code dictionary at most 25 bits wide, yielding a length at most 2^25, or ~33.5 million.

The dictionary is not reset when its width limit is reached. This results in exponential resource usage with increasing database size, eventually becoming prohibitive.

- Format 3/4

Add database name to front of data.

```
Database name -- [4 bytes: big-endian unsigned int, length of following string][_n_ bytes: string]
Record count -- [4 bytes: big-endian unsigned int]
	Record creation date/time -- [4 bytes][_n_ bytes: string from long]
	Record index -- [4 bytes][_n_ bytes: string from signed int]
	Record initial state -- [4 bytes][_n_ bytes: string. Initial text of the record at the time of creation]
	Record modified date/time -- [4 bytes][_n_ bytes: string from long. Date and time of the last change to record]
	Revision count -- [4 bytes][_n_ bytes: string from signed int. Number of changes made to record]
		Revision creation date/time -- [4 bytes][_n_ bytes: string from long. Date the change was made]
		Revision start index -- [4 bytes][_n_ bytes: string from signed int. Index of the change to the record string. Dependant on the state of the record after reconstruction from all previous revisions (see NoteRecord.Reconstruct)]
		Revision substring -- [4 bytes][_n_ bytes: string. The text to insert in the record after removing all text after the revision start index. May be string.Empty]
```

- Format 5/6

Add record UUID to front of record data.

```
Database name -- [4 bytes: big-endian unsigned int, length of following string][_n_ bytes: string]
Record count -- [4 bytes: big-endian unsigned int]
	Record UUID -- [4 bytes][_n_ bytes: string]
	Record creation date/time -- [4 bytes][_n_ bytes: string from long]
	Record index -- [4 bytes][_n_ bytes: string from signed int]
	Record initial state -- [4 bytes][_n_ bytes: string. Initial text of the record at the time of creation]
	Record modified date/time -- [4 bytes][_n_ bytes: string from long. Date and time of the last change to record]
	Revision count -- [4 bytes][_n_ bytes: string from signed int. Number of changes made to record]
		Revision creation date/time -- [4 bytes][_n_ bytes: string from long. Date the change was made]
		Revision start index -- [4 bytes][_n_ bytes: string from signed int. Index of the change to the record string. Dependant on the state of the record after reconstruction from all previous revisions (see NoteRecord.Reconstruct)]
		Revision substring -- [4 bytes][_n_ bytes: string. The text to insert in the record after removing all text after the revision start index. May be string.Empty]
```

- Format 7/8

Add database UUID to front of database data. Add revision UUID to front of revision data.

```
Database UUID -- [4 bytes][_n_ bytes: string]
Database name -- [4 bytes: big-endian unsigned int, length of following string][_n_ bytes: string]
Record count -- [4 bytes: big-endian unsigned int]
	Record UUID -- [4 bytes][_n_ bytes: string]
	Record creation date/time -- [4 bytes][_n_ bytes: string from long]
	Record index -- [4 bytes][_n_ bytes: string from signed int]
	Record initial state -- [4 bytes][_n_ bytes: string. Initial text of the record at the time of creation]
	Record modified date/time -- [4 bytes][_n_ bytes: string from long. Date and time of the last change to record]
	Revision count -- [4 bytes][_n_ bytes: string from signed int. Number of changes made to record]
		Revision UUID -- [4 bytes][_n_ bytes: string]
		Revision creation date/time -- [4 bytes][_n_ bytes: string from long. Date the change was made]
		Revision start index -- [4 bytes][_n_ bytes: string from signed int. Index of the change to the record string. Dependant on the state of the record after reconstruction from all previous revisions (see NoteRecord.Reconstruct)]
		Revision substring -- [4 bytes][_n_ bytes: string. The text to insert in the record after removing all text after the revision start index. May be string.Empty]
```

- Format 9/10

Not yet implemented.

When implemented, this format will compress data with an LZW dictionary that resets once its width limit is reached, preventing runaway resource usage and allowing for much larger databases.
