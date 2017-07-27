import numpy as np

import scipy.ndimage.filters as filters
from skimage import color
from skimage import io

import pywt as pywt

import Helpers as helpers

import colorsys as color
import math
import scipy.misc
import matplotlib as mpl
from matplotlib.mlab import PCA as pca

def loadInputImages():

    highres = io.imread("D:\\Data\\FusionComparison\\highres.bmp", as_grey=True)
    lowRes = io.imread("D:\\Data\\FusionComparison\\lowres.bmp")    

    # Scale lowres to [0, 1] -- highres is already in range from imread
    return highres, np.divide(lowRes, 255)

def doWeightedAverageFusion():

    highres, lowres = loadInputImages()

    hrSizeX = highres.shape[0]
    hrSizeY = highres.shape[1]
    lrSizeX = lowres.shape[0]
    lrSizeY = lowres.shape[1]

    lowresResized = helpers.upscaleImage(lowres, highres)

    fused = np.zeros([hrSizeX, hrSizeY, 3])
    H = np.zeros([hrSizeX, hrSizeY])
    S = np.zeros([hrSizeX, hrSizeY])
    V = np.zeros([hrSizeX, hrSizeY])

    for x in range(hrSizeX):
        for y in range(hrSizeY):            
            H[x,y],S[x,y],V[x,y] = color.rgb_to_hsv(lowresResized[x,y,0], lowresResized[x,y,1], lowresResized[x,y,2])


    # V = helpers.matchHistogram(highres, V)

    meanV = np.mean(V)
    meanHr = np.mean(highres)
    meanSum = meanV + meanHr

    for x in range(hrSizeX):
        for y in range(hrSizeY):

            wa = (V[x,y] * meanV / meanSum) + (highres[x,y] * meanHr / meanSum)
            r,g,b = color.hsv_to_rgb(H[x,y], S[x,y], wa)
            fused[x,y,:] = [r,g,b]

    io.imsave("D:\\Data\\FusionComparison\\fused_wa.bmp", fused)

def doHSVFusion():

    highres, lowres = loadInputImages()

    hrSizeX = highres.shape[0]
    hrSizeY = highres.shape[1]
    lrSizeX = lowres.shape[0]
    lrSizeY = lowres.shape[1]

    lowresResized = helpers.upscaleImage(lowres, highres)

    fused = np.zeros([hrSizeX, hrSizeY, 3])
    H = np.zeros([hrSizeX, hrSizeY])
    S = np.zeros([hrSizeX, hrSizeY])
    V = np.zeros([hrSizeX, hrSizeY])

    for x in range(hrSizeX):
        for y in range(hrSizeY):            
            H[x,y],S[x,y],V[x,y] = color.rgb_to_hsv(lowresResized[x,y,0], lowresResized[x,y,1], lowresResized[x,y,2])


    V = helpers.matchHistogram(highres, V)

    for x in range(hrSizeX):
        for y in range(hrSizeY):

            r,g,b = color.hsv_to_rgb(H[x,y], S[x,y], V[x,y])
            fused[x,y,:] = [r,g,b]

    io.imsave("D:\\Data\\FusionComparison\\fused_hsv.bmp", fused)

def doHSLFusion():
    # Note: HSL is the typical abbreviation but the colorsys fuction switches the position of s and l (HLS)

    highres, lowres = loadInputImages()

    hrSizeX = highres.shape[0]
    hrSizeY = highres.shape[1]
    lrSizeX = lowres.shape[0]
    lrSizeY = lowres.shape[1]

    lowresResized = helpers.upscaleImage(lowres, highres)

    fused = np.zeros([hrSizeX, hrSizeY, 3])
    H = np.zeros([hrSizeX, hrSizeY])
    L = np.zeros([hrSizeX, hrSizeY])
    S = np.zeros([hrSizeX, hrSizeY])

    for x in range(hrSizeX):
        for y in range(hrSizeY):            
            H[x,y],L[x,y],S[x,y] = color.rgb_to_hls(lowresResized[x,y,0], lowresResized[x,y,1], lowresResized[x,y,2])

    L = helpers.matchHistogram(highres, L)

    for x in range(hrSizeX):
        for y in range(hrSizeY):

            r,g,b = color.hls_to_rgb(H[x,y], L[x,y], S[x,y])
            fused[x,y,:] = [r,g,b]

    io.imsave("D:\\Data\\FusionComparison\\fused_hsl.bmp", fused)

def doADWTFusion():

    highres, lowres = loadInputImages()

    hrSizeX = highres.shape[0]
    hrSizeY = highres.shape[1]
    lrSizeX = lowres.shape[0]
    lrSizeY = lowres.shape[1]

    lowresResized = helpers.upscaleImage(lowres, highres)

    H = np.zeros([hrSizeX, hrSizeY])
    L = np.zeros([hrSizeX, hrSizeY])
    S = np.zeros([hrSizeX, hrSizeY])

    for x in range(hrSizeX):
        for y in range(hrSizeY):            
            H[x,y],L[x,y],S[x,y] = color.rgb_to_hls(lowresResized[x,y,0], lowresResized[x,y,1], lowresResized[x,y,2])

    highres = helpers.matchHistogram(highres, L)

    numDecomps = [1, 2, 3, 4, 5, 6]

    for n in numDecomps:

        decompL = pywt.swt2(L, 'haar', n)
        decompP = pywt.swt2(highres, 'haar', n)

        #for i in range(n):
        #    L = np.add(L, decompP[i][0])

        for x in range(hrSizeX):
            for y in range(hrSizeY):
                for i in range(n):
                    decompL[i][0][x, y] = (decompL[i][0][x, y] + decompP[i][0][x, y]) / 2
                    # decompL[1][0][x, y] = decompL[1][0][x, y] + decompP[1][0][x, y]

        L = pywt.iswt2(decompL, 'haar')

        R = np.zeros([hrSizeX, hrSizeY])
        G = np.zeros([hrSizeX, hrSizeY])
        B = np.zeros([hrSizeX, hrSizeY])

        L = helpers.normalizeComponent(L)

        for x in range(hrSizeX):
            for y in range(hrSizeY):
                R[x,y], G[x,y], B[x,y] = color.hls_to_rgb(H[x,y], L[x,y], S[x,y])

        fused = np.empty([hrSizeX,hrSizeY,3])
        fused[:,:,0] = R
        fused[:,:,1] = G
        fused[:,:,2] = B

        #print("Min {0} Max {1}".format(np.min(fused), np.max(fused)))

        #fused[fused < 0] = 0
        #fused[fused > 1] = 1

        io.imsave("D:\\Data\\FusionComparison\\fused_adwt_{0}.bmp".format(n), fused)

def doSDWTFusion():

    highres, lowres = loadInputImages()

    hrSizeX = highres.shape[0]
    hrSizeY = highres.shape[1]
    lrSizeX = lowres.shape[0]
    lrSizeY = lowres.shape[1]

    # highres = np.multiply(highres, 255)

    lowresResized = helpers.upscaleImage(lowres, highres)

    H = np.zeros([hrSizeX, hrSizeY])
    L = np.zeros([hrSizeX, hrSizeY])
    S = np.zeros([hrSizeX, hrSizeY])

    for x in range(hrSizeX):
        for y in range(hrSizeY):            
            H[x,y],L[x,y],S[x,y] = color.rgb_to_hls(lowresResized[x,y,0], lowresResized[x,y,1], lowresResized[x,y,2])

    highres = helpers.matchHistogram(highres, L)
    
    numDecomps = [1, 2, 3, 4, 5, 6]

    for n in numDecomps:
        decompL = pywt.swt2(L, 'haar', n)
        decompP = pywt.swt2(highres, 'haar', n)

        #L = decompP[n - 1][0]

        for x in range(hrSizeX):
            for y in range(hrSizeY):
                for i in range(n):
                    decompL[i][0][x, y] = decompP[i][0][x, y]
                    # decompL[1][0][x, y] = decompP[1][0][x, y]

        L = pywt.iswt2(decompL, 'haar')

        L = helpers.normalizeComponent(L)

        R = np.zeros([hrSizeX, hrSizeY])
        G = np.zeros([hrSizeX, hrSizeY])
        B = np.zeros([hrSizeX, hrSizeY])

        for x in range(hrSizeX):
            for y in range(hrSizeY):
                R[x,y], G[x,y], B[x,y] = color.hls_to_rgb(H[x,y], L[x,y], S[x,y])

        fused = np.empty([hrSizeX,hrSizeY,3])
        fused[:,:,0] = R
        fused[:,:,1] = G
        fused[:,:,2] = B

        #print("Min {0} Max {1}".format(np.min(fused), np.max(fused)))

        #fused[fused < 0] = 0
        #fused[fused > 1] = 1

        io.imsave("D:\\Data\\FusionComparison\\fused_sdwt_{0}.bmp".format(n), fused)

def doPCAFusion():

    highres, lowres = loadInputImages()

    hrSizeX = highres.shape[0]
    hrSizeY = highres.shape[1]
    lrSizeX = lowres.shape[0]
    lrSizeY = lowres.shape[1]

    lowresResized = helpers.upscaleImage(lowres, highres)
    lowresResized = np.reshape(lowresResized, [hrSizeX * hrSizeY, -1])

    # Mean center each column
    means = np.mean(lowresResized, axis=0)
    means = np.tile(means, [hrSizeX * hrSizeY, 1])
    lowresResized = np.subtract(lowresResized, means)

    highRes = np.reshape(highres, [hrSizeX * hrSizeY])

    pcCount = 3

    fused = np.zeros([hrSizeX, hrSizeY, 3])
    cov = np.dot(lowresResized.T, lowresResized) / lowresResized.shape[0]
    eValues, eVectors = np.linalg.eigh(cov)
    key = np.argsort(eValues)[::-1][:pcCount]
    eValues, eVectors = eValues[key], eVectors[:, key]
    u = np.dot(lowresResized, eVectors)

    pc1 = u[:,0]

    highRes = helpers.matchHistogram(highRes, pc1)
    u[:,0] = highRes

    reverted = np.dot(u, eVectors)
    # Reverse mean centering
    reverted = np.add(reverted, means)
    fused = np.reshape(reverted, [hrSizeX, hrSizeY, 3])

    #if np.min(fused) < 0:
    #    fused = np.subtract(fused, np.min(fused))
    #if np.max(fused) > 1:
    #    fused = np.divide(fused, np.max(fused))

    print("Min {0} Max {1}".format(np.min(fused), np.max(fused)))

    fused[fused < 0] = 0
    fused[fused > 1] = 1

    io.imsave("D:\\Data\\FusionComparison\\fused_pca.bmp", fused)

def doLaplaceFusion():
    
    highres, lowres = loadInputImages()

    hrSizeX = highres.shape[0]
    hrSizeY = highres.shape[1]
    lrSizeX = lowres.shape[0]
    lrSizeY = lowres.shape[1]

    lowresResized = helpers.upscaleImage(lowres, highres)

    fused = np.zeros([hrSizeX, hrSizeY, 3])
    H = np.zeros([lrSizeX, lrSizeY])
    L = np.zeros([lrSizeX, lrSizeY])
    S = np.zeros([lrSizeX, lrSizeY])

    for x in range(lrSizeX):
        for y in range(lrSizeY):            
            H[x,y],L[x,y],S[x,y] = color.rgb_to_hls(lowres[x,y,0], lowres[x,y,1], lowres[x,y,2])

    numLevels = int(np.log2(max(hrSizeX / lrSizeX, hrSizeY / lrSizeY))) + 1

    fusionLevel = numLevels - 1

    sHr = [None] * numLevels
    gHr = [None] * numLevels
    lHr = [None] * numLevels

    for i in range(numLevels):

        if i == 0:
            sHr[i] = highres
        else:
            sHr[i] = sHr[i - 1][::2,::2]

        gHr[i] = filters.gaussian_filter(sHr[i], 1.0)
        lHr[i] = sHr[i] - gHr[i]

    matched = helpers.matchHistogram(L, lHr[fusionLevel])

    sRec = [None] * numLevels
    gRec = [None] * numLevels
    lRec = [None] * numLevels

    i = fusionLevel
    while i >= 0:

        if i == fusionLevel:
            sRec[i] = matched + lHr[i]
            # sRec[i] = gHr[i] + lHr[i]
        else:
            sRec[i] = gRec[i] + lHr[i]
        
        if i > 0:
            gRec[i - 1] = filters.gaussian_filter(helpers.emptyUpscale(sRec[i]), 1.0) * 4

        i = i - 1

    H = helpers.upscaleImage(H, sRec[0])
    S = helpers.upscaleImage(S, sRec[0])
    L = sRec[0]

    L = helpers.normalizeComponent(L)

    lmin = np.min(L)
    lmax = np.max(L)

    for x in range(hrSizeX):
        for y in range(hrSizeY):

            red,green,blue = color.hls_to_rgb(H[x,y], L[x,y], S[x,y])
            fused[x,y,:] = [red,green,blue]

    # print("Min {0} Max {1}".format(np.min(fused), np.max(fused)))

    #if np.min(fused) < 0:
    #    fused = np.subtract(fused, np.min(fused))
    #if np.max(fused) > 1:
    #    fused = np.divide(fused, np.max(fused))

    #fused[fused < 0] = 0
    #fused[fused > 1] = 1

    io.imsave("D:\\Data\\FusionComparison\\fused_lpf.bmp", fused)

    #numLevels = [1, 2, 3, 4, 5, 6]
    #for n in numLevels:

    #    fusionLevel = n - 1

    #    s = [None] * n
    #    g = [None] * n
    #    l = [None] * n

    #    for i in range(n):

    #        if i == 0:
    #            s[i] = L
    #        else:
    #            s[i] = s[i - 1][::2,::2]

    #        g[i] = filters.gaussian_filter(s[i], 1.0)
    #        l[i] = s[i] - g[i]

    
    #    matched = helpers.matchHistogram(highres, l[fusionLevel])

    #    for i in range(fusionLevel):
    #        matched = matched[::2,::2]

    #    r = [None] * n
    #    i = fusionLevel
    #    while i >= 0:

    #        if i == fusionLevel:
    #            r[i] = matched + l[i]
    #        else:
    #            r[i] = filters.gaussian_filter(helpers.emptyUpscale(r[i + 1]), 1.0) + l[i]

    #        i = i - 1

    #    H = np.divide(helpers.upscaleImage(H, r[0]), 255)
    #    S = np.divide(helpers.upscaleImage(S, r[0]), 255)

    #    lmin = np.min(r[0])
    #    lmax = np.max(r[0])

    #    if np.min(r[0]) < 0:
    #        r[0] = np.subtract(r[0], np.min(r[0]))
    #    if np.max(r[0]) > 0:
    #        r[0] = np.divide(r[0], np.max(r[0]))

    #    z = 0

    #    for x in range(hrSizeX):
    #        for y in range(hrSizeY):

    #            red,green,blue = color.hls_to_rgb(H[x,y], r[0][x,y], S[x,y])
    #            fused[x,y,:] = [red,green,blue]

    #    fusedMin = np.min(fused)
    #    fusedMax = np.max(fused)

    #    print("Min {0} Max {1}".format(np.min(fused), np.max(fused)))

    #    if np.min(fused) < 0:
    #        fused = np.subtract(fused, np.min(fused))
    #    if np.max(fused) > 1:
    #        fused = np.divide(fused, np.max(fused))

    #    #fused[fused < 0] = 0
    #    #fused[fused > 1] = 1

    #    io.imsave("D:\\Data\\FusionComparison\\fused_lpf_{0}.bmp".format(n), fused)

if __name__ == "__main__":
    
    # doWeightedAverageFusion()
    # doHSVFusion()
    # doHSLFusion()
    # doADWTFusion()
    # doSDWTFusion()
    # doPCAFusion()
    doLaplaceFusion()