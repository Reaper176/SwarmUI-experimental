I have made this fork to add some basic functionality to the ui.

# Added Functionality

## Added search options to models tabs.

<br/>

Just a couple of small changes to search functionality. You can now pick what field you would like to search. Also, relevance is now a thing, so when you type a word, that thing will be sorted to the top of the list. Some highlighting should also happen when in the “everything” mode.

<br/>

<img width="265" height="153" alt="image" src="https://github.com/user-attachments/assets/b60698a6-78fc-4001-80c0-dcac5a7825b2" />

<br/>

<img width="342" height="148" alt="image" src="https://github.com/user-attachments/assets/82fa2e31-527c-44b5-b6aa-2aeadf906026" />

<br/>

## Added Inpainting and Image editing tab.

<br/>

The Image editing tools were a little lacking, also a bit squished, and felt cramped. I have added them to a separate tab. (If you prefer the old way, it remains in place.) Under the new image editing tab, several new options have been added, including saturation, light value, and color balance for shadows, midtones, and highlights. These adjustments are per layer. This is very much a WIP, but I will try to avoid pushing anything that breaks the normal way of doing things. Many more changes are coming. I intend to turn this tab into a tool capable of getting most of the basic alterations that people normally do, all contained in one place, so you don't need to open it in Krita, gimp, or Photoshop just to finalize your edits.

<br/>

## Some of the things I will be adding are:

<br/>

1. Luts
2. Proper brushes with flow, scaling, transparency, and potentially pressure control.
3. Better photo-bashing tools for cropping, rotating, flipping, etc.
4. Better path tools.
5. Much more.

<br/>

<img width="2546" height="1423" alt="image" src="https://github.com/user-attachments/assets/d041eb2b-d39f-485b-b428-e87649201eee" />

<br/>

## Added Save Image Option For Inpainting.

<br/>

When generating images or inpainting, you often do not want to save every single image or iteration of the inpainting process. I added a manual save button that saves a selected image while respecting user-defined settings, such as the save folder and naming conventions, under the Settings tab. This allows you to toggle the “do not save” option on and only save the things you want. This differs from the current “Download” option in that it acknowledges the user's settings and is a single click, eliminating the need to click through context menus or dropdowns. Note: This also works for normal image generation, allowing you to queue a batch of 50 and review them to select the ones you want to save.

<br/>

<img width="2256" height="1326" alt="image" src="https://github.com/user-attachments/assets/98c13717-7e54-4ded-be63-f4bf6daafc49" />

<br/>

<img width="2256" height="1298" alt="image" src="https://github.com/user-attachments/assets/534cb447-dce3-4d73-a1bf-257c3ce15af7" />

## Multi-image selection in history

<br/>

This places check boxes in the top left corner of each image in the history to allow for the easy removal of multiple images at once.

<br/>

<img width="1644" height="482" alt="image" src="https://github.com/user-attachments/assets/7f8ef9e4-c673-4c8b-8743-d8c4dd4d9895" />

<br/>

<br/>

## Lora trigger tag usability.

<br/>

When importing Loras often enough, the trigger phrases section is filled with multiple tags. I have implemented the standard functionality available in all other interfaces to select individual tags from the list and copy them instead of copying the entire trigger phrase section. (If nothing is selected, the entire section is still copied for ease of use. This works under the LORA tab as well as when clicking on the active LORA under the center area. The colors of the bubbles should obey theming.

<br/>

<br/>

 

<br/>

## Exposed perimeters to allow for inpainting resolution setting

<br/>

This lets you set a resolution manually for inpainting so you can do a part of an image at a higher resolution than the rest of the image or just override Swarm's default resolution without going into the model's metadata.

<br/>

<img width="403" height="548" alt="image" src="https://github.com/user-attachments/assets/c46c786f-0254-42be-96eb-13562543c5ed" />

<br/>

## Pre-upscale refiner

<br/>

When doing upscales, it is well known that you will simply amplify the errors that are already present. This is somewhat offset by doing a refiner pass after the fact. However, doing a refiner pass to add detail and correct mistakes before upscalling makes the after-upscale refiner even more effective.

<br/>

<img width="602" height="138" alt="image" src="https://github.com/user-attachments/assets/b80eb9a2-18af-494b-90ac-fa1cf06e33f1" />

<br/>

## Added better save folder override

Currently, in Swarm, you have to manually type out the save path override when doing so from the generate tab. I don’t like this. I have added an option directly under this current implementation that shows all your current folders, and also the option to add a new folder if you want that. Yes, searching works.

<br/>

<img width="541" height="352" alt="image" src="https://github.com/user-attachments/assets/fe1cabb5-e185-4d43-96c9-4f0bcd6fdb9c" />

<br/>

## Added a hide image function.

From time to time, I make images that I do not wish to show when streaming or while at work. So I added a simple show hide option that also uses the bulk image selection check boxes the same way that the remove image options do. This allows for easy search and bulk hide operations.

<br/>

<img width="2047" height="81" alt="image" src="https://github.com/user-attachments/assets/4174abe0-a861-4fab-a883-7b1926b2275f" />

<br/>

## FIXES:

<br/>

Nothing big, but when resizing images at specific resolutions with spacific ui positioning, sometimes the center image will “jitter” or resize repeatedly over and over. This was annoying and should be fixed now. I will try to keep this repo in parity with the original, but may be a day behind.

<br/>

Fixed my own save image button. It now saves metadata correctly.

<br/>

Fixed issues with large history loads. Now retries once and then gives you a manual option after that. This helps when loading large amounts of images (100k+). Most helpful when loading off a spinning drive.

<br/>

Adjusted the drag and drop image functionality. Dropped images now snap to the base image instead of dropping wherever the cursor is. The base image is currently defined by the image at the bottom of the stack. This allows you to change what is considered the base image.

When resizing images, they will also snap to the edges of the base image, following the same logic.

<br/>

SwarmUI

<br/>

Formerly known as StableSwarmUI.

<br/>

A Modular AI Image Generation Web-User-Interface, with an emphasis on making powertools easily accessible, high performance, and extensibility. Supports AI image models (Stable Diffusion, Z-Image, Flux, Qwen Image, etc.), and AI video models (Wan, Hunyuan Video, etc.), with plans to support, e.g., audio and more in the future.

<br/>

ui-screenshot

<br/>

* Discord Community: Join the Discord to discuss the project, get support, see announcements, etc.
* Announcements: Follow the Feature Announcements Thread for updates on new features.
* General documentation: /docs folder
* Website: SwarmUI.net


---

Status

This project is in Beta status. This means for most tasks, Swarm has excellent tooling available to you, but there is much more planned. Swarm is recommended as an ideal UI for most users, beginners, and pros alike. There are still some things to be worked out.

Beginner users will love Swarm’s primary Generate tab interface, making it easy to generate anything with a variety of powerful features. Advanced users may favor the Comfy Workflow tab to get the unrestricted raw graph, but will still have reason to come back to the Generate tab for convenience features (image editor, auto-workflow-generation, etc) and powertools (e.g., Grid Generator).

Those interested in helping push Swarm from Beta to a full, ready-for-anything, perfected Release status are welcome to submit PRs (read the Contributing document first), and you can contact us here on GitHub or on Discord. I highly recommend reaching out to ask about plans for a feature before PRing it. There may already be specific plans or even a work in progress.

Key feature targets not yet implemented:

* Better mobile browser support
* Full detail “Current Model” display in UI, separate from the model selector (probably as a tab within the batch sidebar?)
* LLM-assisted prompting (there’s an extension for it, but LLM control should be natively supported)
* convenient direct-distribution of Swarm as a program (Tauri, Blazor Desktop, or an Electron app?)

Donate

SwarmUI is 100% free and open source forever. If you want to help make sure it keeps pace with the best despite my refusal to paywall access or shove ads down your throat, donate to SwarmUI!

Try It On Google Colab

Google Colab

WARNING: Google Colab does not necessarily allow remote WebUIs, particularly for free accounts, for use at your own risk.

Colab link if you want to try Swarm: https://colab.research.google.com/github/mcmonkeyprojects/SwarmUI/blob/master/colab/colab-notebook.ipynb

Run it on a Cloud GPU Provider

Runpod

Runpod template (note: maintained by third-party contributor nerdylive123): https://get.runpod.io/swarmui

Vast.ai

Vast.ai template (readme): https://cloud.vast.ai/?ref_id=62897&creator_id=62897&name=SwarmUI

Note that it may take several minutes to start up the first time. Check the container logs to see setup progress. Check the template ? info for hints on how to use.

Installing on Windows

Note: if you’re on Windows 10, you may need to manually install git and DotNET 8 SDK first. (Windows 11, this is automated.

* Download the Install-Windows.bat file, store it somewhere you want to install it (not Program Files), and run it.
  * It should open a command prompt and install itself.
  * If it closes without going further, try running it again; it sometimes needs to run twice. (TODO: Fix that)
  * It will place an icon on your desktop that you can use to re-launch the server at any time.
  * When the installer completes, it will automatically launch the SwarmUI server and open a browser window to the install page.
  * Follow the installation instructions on the page.
  * After you submit, be patient; some of the installation processing takes a few minutes (downloading models, etc.).

(TODO): Even easier self-contained pre-installer, a .msi or .exe that provides a general install screen and lets you pick a folder and all.

Alternate Manual Windows Install

* Install git from https://git-scm.com/download/win
* Install DotNET 8 SDK from https://dotnet.microsoft.com/en-us/download/dotnet/8.0 (Make sure to get the SDK x64 for Windows)
* Open a terminal to the folder you want Swarm in and run git clone https://github.com/Reaper176/SwarmUI-experimental.
* Open the folder and run launch-windows.bat

Installing on Linux

Prereqs

* Install git and Python 3 via your OS package manager if they are not already installed (make sure to include pip and venv on distros that do not include them in Python directly)
  * For example, on some Ubuntu (desktop) versions, sudo apt install git python3-pip python3-venv, or you may need https://launchpad.net/~deadsnakes/+archive/ubuntu/ppa
  * For Debian or Ubuntu Server, sudo apt install git python3-full
  * You’ll want Python 3.11 or 3.12. Things should also work fine with 3.10. Do not use 3.13.
  * Make sure python3.11 -m pip --version returns a valid package

Linux Easy Install

* Download the install-linux.sh file, store it somewhere you want to install it, and run it
  * If you like terminals, you can open a terminal to the folder and run the following commands: (Yes, this link is still current):
    * wget https://github.com/Reaper176/SwarmUI-experimental/releases/download/0.6.5-Beta/install-linux.sh -O install-linux.sh
    * chmod +x install-linux.sh
* Run the ./install-linux.sh script, and it will install everything for you and eventually open the webpage in your browser.
* Follow the installation instructions on the page.

Linux Manual Install

* Install DotNET 8 using the instructions at https://dotnet.microsoft.com/en-us/download/dotnet/8.0 (you need dotnet-sdk-8.0, as that includes all relevant sub-packages)
* Open a shell terminal and cd to a directory you want to install into
* Run shell commands:
  * git clone https://github.com/Reaper176/SwarmUI-experimental
  * cd SwarmUI
  * ./launch-linux.sh
  * or if running on a headless server, ./launch-linux.sh --launch_mode none --host 0.0.0.0 and/or swap host for cloudflared
* open http://localhost:7801/Install (if it doesn’t launch itself)
* Follow the installation instructions on the page.

Linux Install Notes

* You can, at any time in the future, run the launch-linux.sh script to re-launch Swarm.
* If the page doesn’t open itself, you can manually open http://localhost:7801.

(TODO): Maybe outlink a dedicated document with per-distro details and whatever. Maybe also make a one-click installer for Linux? Can we remove the global Python install prereq?

Installing on Mac

Note: You can only run SwarmUI on Mac computers with M-Series Apple silicon processors (e.g., M1, M2, …).

* Open Terminal.
* Ensure your brew packages are updated with brew update.
* Verify your brew installation with brew doctor. You should not see any error in the command output.
* Install .NET for macOS: brew install dotnet.
* If you don’t have Python, install it: brew install python@3.11 and brew install virtualenv
  * Python 3.11, 3.12, and 3.10 are all fine. 3.13 is not; do not use 3.13.
* Change the directory (cd) to the folder where you want to install SwarmUI.
* Clone the SwarmUI GitHub repository: git clone https://github.com/mcmonkeyprojects/SwarmUI.
* cd SwarmUI and run the installation script: ./launch-macos.sh.
* Wait for the web browser to open, and follow the installation instructions on the page.

Installing With Docker

See Docs/Docker.md for detailed instructions on using SwarmUI in Docker.

Documentation

See the documentation folder.

Motivations

The “Swarm” name is in reference to the original key function of the UI: enabling a ‘swarm’ of GPUs to all generate images for the same user at once (especially for large grid generations). This is just the feature that inspired the name and not the end-all of what Swarm is.

The overall goal of SwarmUI is to be a full-featured one-stop shop for all things Stable Diffusion.

See the motivations document for motivations on technical choices.

Legal

This project:

* embeds a copy of 7-Zip (LGPL).
* has the ability to auto-install ComfyUI (GPL).
* has the option to use as a backend AUTOMATIC1111/stable-diffusion-webui (AGPL).
* can automatically install christophschuhmann/improved-aesthetic-predictor (Apache2) and yuvalkirstain/PickScore (MIT).
* can automatically install git-for-windows (GPLv2).
* can automatically install MIT/BSD/Apache2/PythonSoftwareFoundation pip packages: spandrel, dill, imageio-ffmpeg, opencv-python-headless, matplotlib, rembg, kornia, Cython
* can automatically install Ultralytics (AGPL) for YOLOv8 face detection (i.e., SwarmYoloDetection node or <segment:yolo-...> syntax usage may become subject to AGPL terms),
* can automatically install InsightFace (MIT) for IP Adapter - Face support
* uses JSON.NET (MIT), FreneticUtilities (MIT), LiteDB (MIT), ImageSharp (Apache2 under open-source Split License)
* embeds copies of web assets from Bootstrap (MIT), Select2 (MIT), JQuery (MIT), exif-reader (MPL-2.0).
* contains some icons from Cristian Munoz (CC-BY-4.0), the font Inter by RSMS (OFL), Unifont by GNU (OFL), and Material Symbols Outlined by Google (Apache2).
* can be used to install some custom node packs, which have individual license notices for any non-pure-FOSS licenses before installation.
* supports user-built extensions, which may have their own licenses or legal conditions.

SwarmUI itself is under the MIT license; some usages may be affected by the GPL variant licenses of connected projects listed above, and note that any models used have their own licenses.

Previous License

(For updates prior to June 2024)

The MIT License (MIT)
Copyright © 2024 Stability AI

License

The MIT License (MIT)

Copyright © 2024-2026 Alex “mcmonkey” Goodwin

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the “Software”), to deal
in the Software without restriction, including, without limitation, the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES, OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT, OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
