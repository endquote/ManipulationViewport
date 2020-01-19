ManipulationViewport
============

By default, a ScatterViewItem can be scaled as large as the display. If the content is an image, the image will scale up with it. This works well, but if the application has multiple users at the same time, one person could scale an image up and occlude everything the other user was working with.

A solution to that would be to limit the size of the ScatterViewItem, but allow the content within it to be manipulated separately from the container. Something like this video: https://vimeo.com/385845169
