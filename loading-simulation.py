import random
from statistics import mean, median, stdev, quantiles, multimode

# in tossing balls randomly into k bins.
# calculate the distribution of n values
# where n is the minimum number of balls tossed for which
# 1. n >= k (this ensures the possibility of filling every bin at least once)
# 2. there is a 95% probability that a bin is non-empty AND contains at least n/2k balls (ie that balls are roughly evenly distributed from 50% of average load and above). 

def calculateCdf(data, x):
    value = 0
    for i in range(len(data)):
        if data[i] <= x:
            value += 1
    return value / len(data)

def tossUntilNIsFound(k):
    bins = list(0 for x in range(k))
    N = 1
    while True:
        nextBinIdx = random.randrange(k)
        bins[nextBinIdx] += 1
        #print(f"{N}. {bins}")
        if isConditionMet(bins, N):
            return bins
        N += 1
    
def isConditionMet(bins, N):
    if N < len(bins):
        return False
    minCnt = max(1, N/(2.0 * len(bins)))
    cdf = 1 - calculateCdf(bins, minCnt)
    return cdf >= 0.95

allBins = []
minServerCounts = []
#print("bin distribution:")
for serverCount in range(2, 11):
    for i in range(10):
        bins = tossUntilNIsFound(serverCount)
        #print(bins)   
        minServerCounts.append(round(sum(bins)/len(bins), 1))
        allBins.append(bins)

print(f"bin count distribution: {minServerCounts}")

print(f"median load = {round(median(minServerCounts), 1)}")

loadQuartiles = quantiles(minServerCounts, method='inclusive')
print(f"load quartiles = {[round(q ,1) for q in loadQuartiles]}")

print(f"modes = {[round(m, 1) for m in multimode(minServerCounts)]}")
print(f"min/max = {round(min(minServerCounts), 1)}/{round(max(minServerCounts), 1)}")

avgLoad = mean(minServerCounts)
std = stdev(minServerCounts, avgLoad)
print(f"mean load = {round(avgLoad, 1)}, std = {round(std, 1)}")
