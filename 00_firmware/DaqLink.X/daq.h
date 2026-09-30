/* 
 * File:   daq.h
 * Author: user
 *
 * Created on 2026年9月30日, 下午 2:30
 */

#ifndef DAQ_H
#define	DAQ_H

#include <stdint.h>

#define DAQ_DEFAULT_RATE    100 /* Hz */

#ifdef	__cplusplus
extern "C" {
#endif

    void daq_init(void);
    void daq_start(void);
    void daq_stop(void);

#ifdef	__cplusplus
}
#endif

#endif	/* DAQ_H */

