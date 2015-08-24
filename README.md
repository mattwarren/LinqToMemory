# Linq to Memory
Linq-to-Memory rather than Linq-to-Objects see [Rico Mariani's excellent blog post](http://blogs.msdn.com/b/ricom/archive/2015/01/02/quot-linq-to-memory-quot-another-old-idea.aspx) that explains the idea

### What is it?
> Basically, it’s this: rather than design your data structures the way they taught you in your CS2xx class, **design them like you were going to store them in a SQL database**.  In fact, you are likely to be well served by using a schema designer.  Then store them exactly like that in memory.  RAM is the new disk.  Cache is the new RAM.

> In fact, I suggested at that time that we write a thing I called “Linq To Memory” – a stark contrast from “Linq to Objects” to help facilitate this.  Basically Linq to Memory was a hypotheticial thing that would be more like an in-memory-database with “tables” that were based on **dense structures like b-tree** and such but no query language other than Linq needed.

So it will be using a B+Tree, something like this (image from [How does a relational database work](http://coding-geek.com/how-databases-work)):

![Image of B+Tree](http://coding-geek.com/wp-content/uploads/2015/08/database_index.png.pagespeed.ce.Ppq1ie22mj.png)

### Why?
> Note that this design actively **discourages the forest of pointers that you normally get in OOP** and yet you get a nice object model to work with (like Linq to SQL gives you) with the slices you are actually working with hydrated into objects and the other parts densely stored.

### How?
I'm going to attempt to build a version of this, using the RavenDB [Voron datastore](https://github.com/ravendb/ravendb/tree/master/Raven.Voron) and the underlying storage system. See [How Voron works: Insight into the new RavenDB storage engine](http://www.slideshare.net/ayenderahien/voron) and [Hello Voron](http://ayende.com/blog/163458/hello-voron) for more information.

