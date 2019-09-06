## Project Changelog

<a name="1.0.0"/>
# 1.0.0 (2018-07-02)

*Features*
* Uploads the contents of a block blob by issuing several PutBlock commands in parallel.
* Allows for the upload of a single blob to be divided across multiple instances of this application.
* The controlling instance of this application (index 0) verifies all blocks have been uploaded before committing them via PutBlockList.

*Bug Fixes*

*Breaking Changes*
